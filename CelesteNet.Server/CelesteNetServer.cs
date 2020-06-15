using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Cecil;
using Mono.Options;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
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

        public readonly CelesteNetServerSettings Settings;

        public readonly DataContext Data;
        public readonly TCPUDPServer TCPUDP;

        public readonly HashSet<CelesteNetConnection> Connections = new HashSet<CelesteNetConnection>();

        public bool Initialized = false;
        public readonly List<CelesteNetServerModuleWrapper> ModuleWrappers = new List<CelesteNetServerModuleWrapper>();
        public readonly List<CelesteNetServerModule> Modules = new List<CelesteNetServerModule>();
        public readonly Dictionary<Type, CelesteNetServerModule> ModuleMap = new Dictionary<Type, CelesteNetServerModule>();
        public readonly FileSystemWatcher ModulesFSWatcher;

        public readonly DetourModManager DetourModManager;

        public uint PlayerCounter = 1;
        public readonly Dictionary<CelesteNetConnection, CelesteNetPlayerSession> PlayersByCon = new Dictionary<CelesteNetConnection, CelesteNetPlayerSession>();
        public readonly Dictionary<uint, CelesteNetPlayerSession> PlayersByID = new Dictionary<uint, CelesteNetPlayerSession>();

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

            Initialized = true;
            lock (Modules) {
                foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers) {
                    Logger.Log(LogLevel.INF, "main", $"Initializing module {wrapper.ID}");
                    wrapper.Module?.Init(wrapper);
                }
            }

            ModulesFSWatcher = new FileSystemWatcher {
                Path = Path.GetFullPath(Settings.ModuleRoot),
                Filter = "*.dll",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };

            ModulesFSWatcher.Changed += (sender, args) => QueuedTaskHelper.Do("ReloadModuleAssembly:" + args.FullPath, () => {
                lock (Modules)
                    foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers)
                        if (args.FullPath == wrapper.AssemblyPath)
                            wrapper.Reload();
            });

            ModulesFSWatcher.EnableRaisingEvents = true;

            TCPUDP = new TCPUDPServer(this);
        }

        public void Start() {
            if (IsAlive)
                return;

            Logger.Log(LogLevel.CRI, "main", "Startup");
            IsAlive = true;

            lock (Modules) {
                foreach (CelesteNetServerModule module in Modules) {
                    Logger.Log(LogLevel.INF, "main", $"Starting module {module.Wrapper?.ID ?? module.ToString()}");
                    module.Start();
                }
            }

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

            TCPUDP.Dispose();
        }


        public void RegisterModule(string path) {
            ModuleWrappers.Add(new CelesteNetServerModuleWrapper(this, path));

            Reload:
            foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers) {
                if (wrapper.Module != null ||
                    !wrapper.References.All(ModuleWrappers.Where(other => other.Module != null).Select(other => other.ID).Contains))
                    continue;

                wrapper.Load();
                if (Initialized && wrapper.Module != null) {
                    Logger.Log(LogLevel.INF, "main", $"Initializing module {wrapper.ID} (late)");
                    wrapper.Module.Init(wrapper);
                    if (IsAlive) {
                        Logger.Log(LogLevel.INF, "main", $"Starting module {wrapper.ID} (late)");
                        wrapper.Module.Start();
                    }
                }
                goto Reload;
            }
        }

        public T Get<T>() where T : class {
            lock (Modules) {
                if (ModuleMap.TryGetValue(typeof(T), out CelesteNetServerModule? module))
                    return module as T ?? throw new Exception($"Incompatible types: Requested {typeof(T).FullName}, got {module.GetType().FullName}");

                foreach (CelesteNetServerModule other in Modules)
                    if (other is T otherT) {
                        ModuleMap[typeof(T)] = other;
                        return otherT;
                    }
            }

            throw new Exception($"Invalid module type: {typeof(T).FullName}");
        }


        public DataPlayerInfo? GetPlayerInfo(CelesteNetConnection con) {
            CelesteNetPlayerSession? player;
            lock (Connections)
                if (!PlayersByCon.TryGetValue(con, out player))
                    return null;
            return player.PlayerInfo;
        }


        public void HandleConnect(CelesteNetConnection con) {
            Logger.Log(LogLevel.INF, "main", $"New connection: {con}");
            con.SendKeepAlive = true;
            lock (Connections)
                Connections.Add(con);
            con.OnDisconnect += HandleDisconnect;
            // FIXME: Control.BroadcastCMD("update", "/status");
        }

        public void HandleDisconnect(CelesteNetConnection con) {
            Logger.Log(LogLevel.INF, "main", $"Disconnecting: {con}");

            lock (Connections)
                Connections.Remove(con);

            CelesteNetPlayerSession? session;
            lock (Connections)
                PlayersByCon.TryGetValue(con, out session);

            session?.Dispose();

            // FIXME: if (session == null)
                // FIXME: Control.BroadcastCMD("update", "/status");
        }

        public void Broadcast(DataType data) {
            lock (Connections) {
                foreach (CelesteNetConnection con in Connections) {
                    try {
                        con.Send(data);
                    } catch (Exception e) {
                        // Whoops, it probably wasn't important anyway.
                        Logger.Log(LogLevel.DEV, "main", $"Broadcast failed:\n{data}\n{con}\n{e}");
                    }
                }
            }
        }

        public void Broadcast(DataType data, params CelesteNetConnection[] except) {
            lock (Connections) {
                foreach (CelesteNetConnection con in Connections) {
                    if (except.Contains(con))
                        continue;
                    try {
                        con.Send(data);
                    } catch (Exception e) {
                        // Whoops, it probably wasn't important anyway.
                        Logger.Log(LogLevel.DEV, "main", $"Broadcast failed:\n{data}\n{con}\n{e}");
                    }
                }
            }
        }

        #region Handlers

        #endregion

    }
}
