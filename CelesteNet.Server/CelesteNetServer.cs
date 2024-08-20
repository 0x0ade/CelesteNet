using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.CelesteCompat;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetServer : IDisposable {

        public readonly DateTime StartupTime;
        public readonly long Timestamp;

        public readonly CelesteNetServerSettings Settings;

        public readonly DataContext Data;
        public readonly NetPlusThreadPool ThreadPool;
        public readonly PacketDumper PacketDumper;

        public UserData UserData;

        public readonly Channels Channels;

        public bool Initialized = false;
        public readonly List<CelesteNetServerModuleWrapper> ModuleWrappers = new();
        public readonly List<CelesteNetServerModule> Modules = new();
        public readonly Dictionary<Type, CelesteNetServerModule> ModuleMap = new();
        public readonly FileSystemWatcher ModulesFSWatcher;

        public int PlayerCounter = 0;
        public readonly TokenGenerator ConTokenGenerator = new();
        public readonly RWLock ConLock = new();
        public readonly HashSet<CelesteNetConnection> Connections = new();
        public readonly BlockingCollection<CelesteNetConnection> SafeDisposeQueue = new();
        public readonly HashSet<CelesteNetPlayerSession> Sessions = new();
        public readonly ConcurrentDictionary<CelesteNetConnection, CelesteNetPlayerSession> PlayersByCon = new();
        public readonly ConcurrentDictionary<uint, CelesteNetPlayerSession> PlayersByID = new();
        private readonly System.Timers.Timer HeartbeatTimer, PingRequestTimer, AvatarSendTimer;

        public float CurrentTickRate { get; private set; }
        private float NextTickRate;

        private readonly CancellationTokenSource ShutdownTokenSrc = new();

        private bool _IsAlive;
        public bool IsAlive {
            get => _IsAlive;
            set {
                if (_IsAlive == value)
                    return;
                if (value && ShutdownTokenSrc.IsCancellationRequested)
                    throw new InvalidOperationException("Can't revive the server!");

                _IsAlive = value;
                if (!_IsAlive)
                    ShutdownTokenSrc.Cancel();
            }
        }

        public CelesteNetServer()
            : this(new()) {
        }

        public CelesteNetServer(CelesteNetServerSettings settings) {
            StartupTime = DateTime.UtcNow;
            Timestamp = StartupTime.Ticks / TimeSpan.TicksPerMillisecond;

            Settings = settings;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
                if (args.Name == null)
                    return null;

                AssemblyName name = new(args.Name);
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

            Data = new();

            Logger.Log(LogLevel.INF, "data", "Rescanning all data types");
            Data.IDToDataType.Clear();
            Data.DataTypeToID.Clear();

            Data.RescanDataTypes(CelesteNetUtils.GetTypes());

            Data.RegisterHandlersIn(this);

            PacketDumper = new(this);

            Channels = new(this);

            UserData = new FileSystemUserData(this);

            Initialized = true;
            lock (Modules) {
                foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers) {
                    Logger.Log(LogLevel.INF, "module", $"Initializing {wrapper.ID}");
                    wrapper.Module?.Init(wrapper);
                }
            }

            ModulesFSWatcher = new() {
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

            int numThreads = Settings.NetPlusThreadPoolThreads;

            if (numThreads < 0 && Environment.ProcessorCount > 0)
                numThreads = Environment.ProcessorCount;

            if (numThreads < 5) {
                // not even sure if this'll 100% work, just basing this off the ThreadPool.Scheduler.AddRole calls in Start() further down
                numThreads = 5;
                Logger.Log(LogLevel.WRN, "module", $"Could not determine thread count, or set too low! (NetPlusThreadPoolThreads = {Settings.NetPlusThreadPoolThreads} / Environment.ProcessorCount = {Environment.ProcessorCount}). Defaulting to {numThreads}.");
            }
            ThreadPool = new(numThreads, Settings.NetPlusMaxThreadRestarts, Settings.HeuristicSampleWindow, Settings.NetPlusSchedulerInterval, Settings.NetPlusSchedulerUnderloadThreshold, Settings.NetPlusSchedulerOverloadThreshold, Settings.NetPlusSchedulerStealThreshold);

            HeartbeatTimer = new(Settings.HeartbeatInterval);
            HeartbeatTimer.Elapsed += (_, _) => DoHeartbeatTick();

            PingRequestTimer = new(Settings.PingRequestInterval);
            PingRequestTimer.Elapsed += (_, _) => SendPingRequests();

            AvatarSendTimer = new(Settings.AvatarQueueInterval);
            AvatarSendTimer.Elapsed += (_, _) => ClearAvatarQueues();
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

            IPEndPoint serverEP = new(IPAddress.IPv6Any, Settings.MainPort);
            CelesteNetTCPUDPConnection.Settings tcpUdpConSettings = new() {
                MaxPacketSize = Settings.MaxPacketSize,
                MaxQueueSize = Settings.MaxQueueSize,
                MergeWindow = Settings.MergeWindow,
                MaxHeartbeatDelay = Settings.MaxHeartbeatDelay,
                HeartbeatInterval = Settings.HeartbeatInterval,
                UDPAliveScoreMax = Settings.UDPAliveScoreMax,
                UDPDowngradeScoreMin = Settings.UDPDowngradeScoreMin,
                UDPDowngradeScoreMax = Settings.UDPDowngradeScoreMax,
                UDPDeathScoreMin = Settings.UDPDeathScoreMin,
                UDPDeathScoreMax = Settings.UDPDeathScoreMax
            };

            Logger.Log(LogLevel.INF, "server", $"Starting server on {serverEP}");
            ThreadPool.Scheduler.AddRole(new HandshakerRole(ThreadPool, this));
            ThreadPool.Scheduler.AddRole(new TCPUDPSenderRole(ThreadPool, this, serverEP));
            ThreadPool.Scheduler.AddRole(new TCPReceiverRole(ThreadPool, this, (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Settings.TCPRecvUseEPoll) ? new TCPEPollPoller() : new TCPFallbackPoller()));
            ThreadPool.Scheduler.AddRole(new UDPReceiverRole(ThreadPool, this, serverEP, ThreadPool.Scheduler.FindRole<TCPUDPSenderRole>()?.UDPSocket));
            ThreadPool.Scheduler.AddRole(new TCPAcceptorRole(ThreadPool, this, serverEP, ThreadPool.Scheduler.FindRole<HandshakerRole>()!, ThreadPool.Scheduler.FindRole<TCPReceiverRole>()!, ThreadPool.Scheduler.FindRole<UDPReceiverRole>()!, ThreadPool.Scheduler.FindRole<TCPUDPSenderRole>()!, tcpUdpConSettings));

            HeartbeatTimer.Start();
            PingRequestTimer.Start();
            AvatarSendTimer.Start();
            CurrentTickRate = NextTickRate = Settings.MaxTickRate;
            ThreadPool.Scheduler.OnPreScheduling += AdjustTickRate;

            Logger.Log(LogLevel.CRI, "main", "Ready");
        }

        public void Wait() {
            foreach (CelesteNetConnection con in SafeDisposeQueue.GetConsumingEnumerable(ShutdownTokenSrc.Token)) {
                if (con.IsAlive) {
                    Logger.Log("main", $"Safe dispose triggered for connection {con}");
                    con.Dispose();
                }
            }
            ShutdownTokenSrc.Dispose();
            SafeDisposeQueue.Dispose();
        }

        public void Dispose() {
            if (!IsAlive)
                return;

            Logger.Log(LogLevel.CRI, "main", "Shutdown");
            IsAlive = false;

            HeartbeatTimer.Dispose();

            ModulesFSWatcher.Dispose();

            lock (Modules) {
                foreach (CelesteNetServerModuleWrapper wrapper in ModuleWrappers.ToArray()) {
                    wrapper.Unload();
                }
            }

            Channels.Dispose();
            ThreadPool.Dispose();
            ConLock.Dispose();

            UserData.Dispose();

            Data.Dispose();
        }


        public void RegisterModule(string path) {
            ModuleWrappers.Add(new(this, path));

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
            using (ConLock.W()) {
                Connections.Add(con);
                OnConnect?.Invoke(this, con);
                con.OnDisconnect += HandleDisconnect;
            }
        }

        public event Action<CelesteNetServer, CelesteNetConnection, CelesteNetPlayerSession?>? OnDisconnect;

        public void HandleDisconnect(CelesteNetConnection con) {
            Logger.Log(LogLevel.INF, "main", $"Disconnecting: {con}");

            using (ConLock.W())
                Connections.Remove(con);

            PlayersByCon.TryGetValue(con, out CelesteNetPlayerSession? session);
            if (session != null) {
                using (ConLock.W()) {
                    Sessions.Remove(session);
                    PlayersByCon.TryRemove(con, out _);
                    PlayersByID.TryRemove(session.SessionID, out _);
                }
                session?.Dispose();
            }

            OnDisconnect?.Invoke(this, con, session);
        }

        public event Action<CelesteNetPlayerSession>? OnSessionStart;

        private int nextSesId = 0;
        public CelesteNetPlayerSession CreateSession(CelesteNetConnection con, string playerUID, string playerName, CelesteNetClientOptions clientOptions) {
            CelesteNetPlayerSession ses;
            using (ConLock.W()) {
                ses = new(this, con, unchecked ((uint) Interlocked.Increment(ref nextSesId)), playerUID, playerName, clientOptions);
                Sessions.Add(ses);
                PlayersByCon[con] = ses;
                PlayersByID[ses.SessionID] = ses;
                ses.Start();
            }
            OnSessionStart?.Invoke(ses);
            return ses;
        }

        public void Broadcast(DataType data) {
            DataInternalBlob blob = new(Data, data);
            using (ConLock.R())
                foreach (CelesteNetConnection con in Connections) {
                    try {
                        con.Send(blob);
                    } catch (Exception e) {
                        // Whoops, it probably wasn't important anyway.
                        Logger.Log(LogLevel.DEV, "main", $"Broadcast (sync) failed:\n{data}\n{con}\n{e}");
                    }
                };
        }

        public void BroadcastAsync(DataType data) {
            DataInternalBlob blob = new(Data, data);
            using (ConLock.R())
                foreach (CelesteNetConnection con in Connections) {
                    Task.Run(() => {
                        try {
                            con.Send(blob);
                        } catch (Exception e) {
                            // Whoops, it probably wasn't important anyway.
                            Logger.Log(LogLevel.DEV, "main", $"Broadcast (async) failed:\n{data}\n{con}\n{e}");
                        }
                    });
                }
        }

        private void DoHeartbeatTick() {
            HashSet<(CelesteNetConnection con, string reason)> disposeCons = new();
            using (ConLock.R())
                foreach (CelesteNetConnection con in new HashSet<CelesteNetConnection>(Connections)) {
                    try {
                        switch (con) {
                            case CelesteNetTCPUDPConnection tcpUdpCon: {
                                string? disposeReason = tcpUdpCon.DoHeartbeatTick();
                                if (disposeReason != null)
                                    disposeCons.Add((tcpUdpCon, disposeReason));
                            } break;
                        }
                    } catch (Exception e) {
                        disposeCons.Add((con, $"Error in heartbeat tick of connection {con}: {e}"));
                    }
                }
            foreach ((CelesteNetConnection con, string reason) in disposeCons) {
                Logger.Log(LogLevel.WRN, "heartbeat", reason);
                try {
                    con.Dispose();
                } catch (Exception e) {
                    Logger.Log(LogLevel.WRN, "main", $"Error disposing connection {con}: {e}");
                }
            }
        }

        private void AdjustTickRate() {
            CurrentTickRate = NextTickRate;

            TCPUDPSenderRole sender = ThreadPool.Scheduler.FindRole<TCPUDPSenderRole>()!;
            float actvRate = ThreadPool.ActivityRate, tcpByteRate = sender.TCPByteRate, udpByteRate = sender.UDPByteRate;
            if (actvRate < Settings.TickRateLowActivityThreshold && tcpByteRate < Settings.TickRateLowTCPUplinkBpSThreshold && udpByteRate < Settings.TickRateLowUDPUplinkBpSThreshold) {
                if (CurrentTickRate < Settings.MaxTickRate) {
                    // Increase the tick rate
                    NextTickRate = Math.Min(Settings.MaxTickRate, CurrentTickRate * 2);
                    Logger.Log(LogLevel.INF, "main", $"Increased the tick rate {CurrentTickRate} TpS -> {NextTickRate} TpS");
                    CurrentTickRate = NextTickRate;
                } else {
                    return;
                }
            } else if (actvRate > Settings.TickRateHighActivityThreshold && tcpByteRate > Settings.TickRateHighTCPUplinkBpSThreshold && udpByteRate > Settings.TickRateHighUDPUplinkBpSThreshold) {
                // Decrease the tick rate
                Logger.Log(LogLevel.INF, "main", $"Decreased the tick rate {CurrentTickRate} TpS -> {NextTickRate} TpS");
                NextTickRate = CurrentTickRate / 2;
            } else {
                return;
            }

            // Broadcast the new tick rate
            BroadcastAsync(new DataTickRate {
                TickRate = NextTickRate
            });
        }

        private void SendPingRequests() {
            using (ConLock.R()) {
                // Send ping requests for supported connections
                foreach (CelesteNetConnection con in Connections) {
                    switch (con) {
                        case ConPlusTCPUDPConnection tcpUdpCon: {
                            tcpUdpCon.SendPingRequest();
                            break;
                        }
                    }
                }

                // Send connection info packets
                foreach (CelesteNetPlayerSession ses in Sessions) {
                    switch (ses.Con) {
                        case ConPlusTCPUDPConnection tcpUdpCon: {
                            DataInternalBlob conInfoPacket = DataInternalBlob.For(Data, new DataConnectionInfo() {
                                Player = ses.PlayerInfo,
                                TCPPingMs = tcpUdpCon.TCPPingMs,
                                UDPPingMs = tcpUdpCon.UDPPingMs
                            });

                            foreach (CelesteNetPlayerSession other in Sessions)
                                other.Con.Send(conInfoPacket);

                            break;
                        }
                    }
                }
            }
        }

        private void ClearAvatarQueues() {
            using (ConLock.R()) {
                foreach (CelesteNetPlayerSession ses in Sessions) {

                    if (ses.AvatarSendQueue.Count > 0) {

                        ses.AvatarSendQueue.RemoveWhere(other => !other.Alive);

                        foreach (CelesteNetPlayerSession other in ses.AvatarSendQueue.Take(Settings.AvatarQueueBatchCount).ToList()) {
                            foreach (DataInternalBlob frag in other.AvatarFragments) {
                                ses.Con.Send(frag);
                            }
                            ses.AvatarSendQueue.Remove(other);
                        }
                        Logger.Log(LogLevel.DBG, "main", $"ClearAvatarQueues: Sent ~{Settings.AvatarQueueBatchCount}/{ses.AvatarSendQueue.Count} players' avatar frags to {ses.Name}");

                    }
                }
            }
        }

        #region Handlers

        #endregion

    }
}
