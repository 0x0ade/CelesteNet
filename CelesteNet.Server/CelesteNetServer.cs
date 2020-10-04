using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Cecil;
using Mono.Options;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetServer : IDisposable {

        public readonly DateTime StartupTime;

        public readonly CelesteNetServerSettings Settings;

        public readonly DataContext Data;
        public readonly TCPUDPServer TCPUDP;

        public string ConnectionFeatures = "";

        public UserData UserData;

        public readonly Channels Channels;

        public bool Initialized = false;
        public readonly List<CelesteNetServerModuleWrapper> ModuleWrappers = new List<CelesteNetServerModuleWrapper>();
        public readonly List<CelesteNetServerModule> Modules = new List<CelesteNetServerModule>();
        public readonly Dictionary<Type, CelesteNetServerModule> ModuleMap = new Dictionary<Type, CelesteNetServerModule>();
        public readonly FileSystemWatcher ModulesFSWatcher;

        public readonly DetourModManager DetourModManager;

        public uint PlayerCounter = 0;
        public readonly RWLock ConLock = new RWLock();
        public readonly HashSet<CelesteNetConnection> Connections = new HashSet<CelesteNetConnection>();
        public readonly HashSet<CelesteNetPlayerSession> Sessions = new HashSet<CelesteNetPlayerSession>();
        public readonly ConcurrentDictionary<CelesteNetConnection, CelesteNetPlayerSession> PlayersByCon = new ConcurrentDictionary<CelesteNetConnection, CelesteNetPlayerSession>();
        public readonly ConcurrentDictionary<uint, CelesteNetPlayerSession> PlayersByID = new ConcurrentDictionary<uint, CelesteNetPlayerSession>();

        private readonly ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

        private bool _IsAlive;
        public bool IsAlive {
            get => _IsAlive;
            set {
                if (_IsAlive == value)
                    return;

                _IsAlive = value;
                if (value)
                    ShutdownEvent.Reset();
                else
                    ShutdownEvent.Set();
            }
        }

        public CelesteNetServer()
            : this(new CelesteNetServerSettings()) {
        }

        public CelesteNetServer(CelesteNetServerSettings settings) {
            StartupTime = DateTime.UtcNow;

            Settings = settings;

            DetourModManager = new DetourModManager();

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                if (args.Name == null)
                    return null;

                AssemblyName name = new AssemblyName(args.Name);
                if (ModuleWrappers.Any(wrapper => wrapper.ID == name.Name))
                    return null;

                string path = Path.Combine(Path.GetFullPath(Settings.ModuleRoot), name.Name + ".dll");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);

                return null;
            };

            foreach (string file in Directory.GetFiles(Path.GetFullPath(Settings.ModuleRoot)))
                if (Path.GetFileName(file).StartsWith("CelesteNet.Server.") && file.EndsWith("Module.dll"))
                    RegisterModule(file);

            Data = new DataContext();
            Data.RegisterHandlersIn(this);

            Channels = new Channels(this);

            UserData = new FileSystemUserData(this);

            Initialized = true;
            lock (Modules) {
                foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers) {
                    Logger.Log(LogLevel.INF, "module", $"Initializing {wrapper.ID}");
                    wrapper.Module?.Init(wrapper);
                }
            }

            ModulesFSWatcher = new FileSystemWatcher {
                Path = Path.GetFullPath(Settings.ModuleRoot),
                Filter = "*.dll",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            ModulesFSWatcher.Error += (sender, args) => {
                Logger.Log(LogLevel.ERR, "module", $"Module file watcher error:\n{args.GetException()}");
            };

            ModulesFSWatcher.Created += OnModuleFileUpdate;
            ModulesFSWatcher.Renamed += OnModuleFileUpdate;
            ModulesFSWatcher.Changed += OnModuleFileUpdate;

            ModulesFSWatcher.EnableRaisingEvents = true;

            TCPUDP = new TCPUDPServer(this);
        }

        private void OnModuleFileUpdate(object sender, FileSystemEventArgs args) {
            Logger.Log(LogLevel.VVV, "module", $"Module file changed: {args.FullPath}, {args.ChangeType}");
            QueuedTaskHelper.Do("ReloadModuleAssembly:" + args.FullPath, () => {
                lock (Modules)
                    foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers)
                        if (args.FullPath == wrapper.AssemblyPath)
                            wrapper.Reload();
            });
        }

        public void Start() {
            if (IsAlive)
                return;

            Logger.Log(LogLevel.CRI, "main", "Startup");
            IsAlive = true;

            lock (Modules) {
                foreach (CelesteNetServerModule module in Modules) {
                    Logger.Log(LogLevel.INF, "module", $"Starting {module.Wrapper?.ID ?? module.ToString()}");
                    module.Start();
                }
            }

            Channels.Start();
            TCPUDP.Start();

            Logger.Log(LogLevel.CRI, "main", "Ready");
        }

        public void Wait() {
            WaitHandle.WaitAny(new WaitHandle[] { ShutdownEvent });
            ShutdownEvent.Dispose();
        }

        public void Dispose() {
            if (!IsAlive)
                return;

            Logger.Log(LogLevel.CRI, "main", "Shutdown");
            IsAlive = false;

            ModulesFSWatcher.Dispose();

            lock (Modules) {
                foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers.ToArray()) {
                    wrapper.Unload();
                }
            }

            Channels.Dispose();
            TCPUDP.Dispose();
            ConLock.Dispose();
        }


        public void RegisterModule(string path) {
            ModuleWrappers.Add(new CelesteNetServerModuleWrapper(this, path));

            Reload:
            foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers) {
                if (wrapper.Module != null ||
                    !wrapper.References.All(ModuleWrappers.Where(other => other.Module != null).Select(other => other.ID).Contains))
                    continue;

                wrapper.Load();
                goto Reload;
            }
        }

        public T Get<T>() where T : class
            => TryGet(out T? module) ? module : throw new Exception($"Invalid module type: {typeof(T).FullName}");

        public bool TryGet<T>([NotNullWhen(true)] out T? moduleT) where T : class {
            lock (Modules) {
                if (ModuleMap.TryGetValue(typeof(T), out CelesteNetServerModule? module)) {
                    moduleT = module as T ?? throw new Exception($"Incompatible types: Requested {typeof(T).FullName}, got {module.GetType().FullName}");
                    return true;
                }

                foreach (CelesteNetServerModule other in Modules)
                    if (other is T otherT) {
                        ModuleMap[typeof(T)] = other;
                        moduleT = otherT;
                        return true;
                    }
            }

            moduleT = null;
            return false;
        }


        public event Action<CelesteNetServer, CelesteNetConnection>? OnConnect;

        public void HandleConnect(CelesteNetConnection con) {
            Logger.Log(LogLevel.INF, "main", $"New connection: {con}");
            con.SendKeepAlive = true;
            using (ConLock.W())
                Connections.Add(con);
            OnConnect?.Invoke(this, con);
            con.OnDisconnect += HandleDisconnect;
        }

        public event Action<CelesteNetServer, CelesteNetConnection, CelesteNetPlayerSession?>? OnDisconnect;

        public void HandleDisconnect(CelesteNetConnection con) {
            Logger.Log(LogLevel.INF, "main", $"Disconnecting: {con}");

            using (ConLock.W())
                Connections.Remove(con);

            PlayersByCon.TryGetValue(con, out CelesteNetPlayerSession? session);
            session?.Dispose();

            OnDisconnect?.Invoke(this, con, session);
        }

        public event Action<CelesteNetPlayerSession>? OnSessionStart;
        internal void InvokeOnSessionStart(CelesteNetPlayerSession session)
            => OnSessionStart?.Invoke(session);

        public void Broadcast(DataType data) {
            DataInternalBlob blob = new DataInternalBlob(Data, data);
            using (ConLock.R())
                foreach (CelesteNetConnection con in Connections) {
                    try {
                        con.Send(blob);
                    } catch (Exception e) {
                        // Whoops, it probably wasn't important anyway.
                        Logger.Log(LogLevel.DEV, "main", $"Broadcast failed:\n{data}\n{con}\n{e}");
                    }
                }
        }

        public void Broadcast(DataType data, params CelesteNetConnection?[]? except) {
            if (except == null) {
                Broadcast(data);
                return;
            }

            DataInternalBlob blob = new DataInternalBlob(Data, data);
            using (ConLock.R())
                foreach (CelesteNetConnection con in Connections) {
                    if (except.Contains(con))
                        continue;
                    try {
                        con.Send(blob);
                    } catch (Exception e) {
                        // Whoops, it probably wasn't important anyway.
                        Logger.Log(LogLevel.DEV, "main", $"Broadcast failed:\n{data}\n{con}\n{e}");
                    }
                }
        }

        #region Handlers

        #endregion

    }
}
