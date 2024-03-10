using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClient : IDisposable {

        public readonly CelesteNetClientSettings Settings;
        public readonly CelesteNetClientOptions Options;

        public readonly DataContext Data;

        public CelesteNetConnection Con;
        public readonly IConnectionFeature[] ConFeatures;
        public volatile bool EndOfStream = false, SafeDisposeTriggered = false;
        public ConnectionErrorCodeException LastConnectionError;

        private bool _IsAlive;
        public bool IsAlive {
            get => _IsAlive;
            set {
                if (_IsAlive == value)
                    return;

                _IsAlive = value;
            }
        }

        public bool IsReady { get; protected set; }
        private readonly ManualResetEventSlim _ReadyEvent;

        public DataPlayerInfo PlayerInfo;

        private readonly object StartStopLock = new();

        private System.Timers.Timer HeartbeatTimer;

        public CelesteNetClient()
            : this(new(), new()) {
        }

        public CelesteNetClient(CelesteNetClientSettings settings, CelesteNetClientOptions options) {
            Settings = settings;
            Options = options;

            Options.AvatarsDisabled = !Settings.ReceivePlayerAvatars;
            Options.ClientID = Settings.ClientID;
            Options.InstanceID = Settings.InstanceID;

            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClient created");

            Data = new();
            Data.RegisterHandlersIn(this);

            _ReadyEvent = new(false);

            // Find connection features
            List<IConnectionFeature> conFeatures = new();
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                try {
                    if (!typeof(IConnectionFeature).IsAssignableFrom(type) || type.IsAbstract)
                        continue;
                } catch (Exception e) {
                    Logger.Log(LogLevel.VVV, "main", $"CelesteNetClient - conFeature threw {e.Message}");
                    continue;
                }

                IConnectionFeature feature = (IConnectionFeature) Activator.CreateInstance(type);
                if (feature == null)
                    throw new Exception($"Cannot create instance of connection feature {type.FullName}");
                Logger.Log(LogLevel.VVV, "main", $"Found connection feature: {type.FullName}");
                conFeatures.Add(feature);
            }
            ConFeatures = conFeatures.ToArray();
        }

        public void Start(CancellationToken token) {
            if (IsAlive) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClient: Start called but IsAlive already");
                return;
            }
            IsAlive = true;

            lock (StartStopLock) {
                if (IsReady) {
                    Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClient: Start called but IsReady already");
                    return;
                }
                Logger.Log(LogLevel.CRI, "main", $"Startup");

                switch (Settings.Debug.ConnectionType) {
                    case ConnectionType.Auto:
                    case ConnectionType.TCPUDP:
                    case ConnectionType.TCP:
                        Logger.Log(LogLevel.INF, "main", $"Connecting via TCP/UDP to {Settings.Host}:{Settings.Port}");

                        // Create a TCP connection
                        // The socket connection code is roughly based off of the TCPClient reference source.
                        // Let's avoid dual mode sockets as there seem to be bugs with them on Mono.
                        List<Socket> sockAll = new();
                        if (Socket.OSSupportsIPv6)
                            sockAll.Add(new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp));
                        if (Socket.OSSupportsIPv4)
                            sockAll.Add(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp));
                        Socket sock = null;
                        LastConnectionError = null;
                        try {
                            uint conToken;
                            IConnectionFeature[] conFeatures;
                            CelesteNetTCPUDPConnection.Settings settings;
                            using (token.Register(() => {
                                foreach (Socket sockTry in sockAll) {
                                    try {
                                        sockTry.Close();
                                    } catch {
                                    }
                                    sockTry.Dispose();
                                }
                            })) {
                                Exception sockEx = null;
                                IPAddress[] addresses = Dns.GetHostAddresses(Settings.Host);
                                Tuple<uint, IConnectionFeature[], CelesteNetTCPUDPConnection.Settings> teapotRes = null;

                                foreach (Socket sockTry in sockAll)
                                    Logger.Log(LogLevel.DBG, "main", $"... with socket type {sockTry.AddressFamily}");

                                foreach (IPAddress address in addresses)
                                    Logger.Log(LogLevel.DBG, "main", $"... to address {address} ({address.AddressFamily})");

                                // Try IPv6 first if possible, then try IPv4.
                                foreach (Socket sockTry in sockAll) {
                                    foreach (IPAddress address in addresses) {
                                        if (sockTry.AddressFamily != address.AddressFamily)
                                            continue;
                                        try {
                                            sock = sockTry;
                                            sock.ReceiveTimeout = sock.SendTimeout = 2000;
                                            sock.Connect(address, Settings.Port);

                                            LastConnectionError = sockEx as ConnectionErrorCodeException;

                                            // Do the teapot handshake here, as a successful "connection" doesn't mean that the server can handle IPv6.
                                            teapotRes = Handshake.DoTeapotHandshake<CelesteNetClientTCPUDPConnection.Settings>(sock, ConFeatures, Settings.NameKey, Options);
                                            Logger.Log(LogLevel.INF, "main", $"Connecting to {address} ({address.AddressFamily}) succeeded");
                                            break;
                                        } catch (Exception e) {
                                            Logger.Log(LogLevel.INF, "main", $"Connecting to {address} ({address.AddressFamily}) failed: {e.GetType()}: {e.Message}");
                                            sock?.ShutdownSafe(SocketShutdown.Both);
                                            sock = null;
                                            teapotRes = null;
                                            sockEx = e;
                                            continue;
                                        }
                                    }
                                    if (sock != null)
                                        break;
                                }

                                // Cleanup all non-used sockets.
                                foreach (Socket sockTry in sockAll) {
                                    if (sockTry == sock)
                                        continue;
                                    try {
                                        sockTry.Close();
                                    } catch (Exception e) {
                                        Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClient Start: sock close caught '{e.Message}'");
                                    }
                                    sockTry.Dispose();
                                }

                                if (sock == null || teapotRes == null) {
                                    if (sockEx == null) {
                                        throw new Exception($"Failed to connect to {Settings.Host}:{Settings.Port}, didn't find any connectable address, no exception (was any address even tried?)");
                                    }
                                    if (sockEx is SocketException sockExSock) {
                                        if (sockExSock.SocketErrorCode == SocketError.WouldBlock || sockExSock.SocketErrorCode == SocketError.TimedOut) {
                                            throw new Exception($"Failed to connect to {Settings.Host}:{Settings.Port}, didn't find any connectable address, last tried address timed out");
                                        }
                                    }
                                    throw new Exception($"Failed to connect to {Settings.Host}:{Settings.Port}, didn't find any connectable address", sockEx);
                                }

                                // Process the teapot handshake
                                conToken = teapotRes.Item1;
                                conFeatures = teapotRes.Item2;
                                settings = teapotRes.Item3;
                                Logger.Log(LogLevel.INF, "main", $"Teapot handshake success: token {conToken} conFeatures '{conFeatures.Select(f => f.GetType().FullName).Aggregate((string) null, (a, f) => (a == null) ? f : $"{a}, {f}")}'");
                                sock.ReceiveTimeout = sock.SendTimeout = (int) (settings.HeartbeatInterval * settings.MaxHeartbeatDelay);
                            }

                            // Create a connection and start the heartbeat timer
                            CelesteNetClientTCPUDPConnection con = new(this, conToken, settings, sock);
                            con.OnDisconnect += _ => Dispose();
                            if (Settings.Debug.ConnectionType == ConnectionType.TCP)
                                con.UseUDP = false;

                            // Initialize the heartbeat timer
                            HeartbeatTimer = new(settings.HeartbeatInterval);
                            HeartbeatTimer.AutoReset = true;
                            HeartbeatTimer.Elapsed += (_, _) => {
                                string disposeReason = con.DoHeartbeatTick();
                                if (disposeReason != null) {
                                    Logger.Log(LogLevel.CRI, "main", disposeReason);
                                    Dispose();
                                }
                            };
                            HeartbeatTimer.Start();

                            // Do the regular connection handshake
                            Handshake.DoConnectionHandshake(Con, conFeatures, token);
                            Logger.Log(LogLevel.INF, "main", $"Connection handshake success");

                            Con = con;
                        } catch (Exception e) {
                            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClient Start: Caught exception, will throw: '{e.Message}'");
                            Con?.Dispose();
                            foreach (Socket sockTry in sockAll) {
                                try {
                                    sockTry.Close();
                                } catch (Exception se) {
                                    Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClient Start: sock close caught '{se.Message}'");
                                }
                                sockTry.Dispose();
                            }
                            Con = null;
                            throw;
                        }

                        break;

                    default:
                        throw new NotSupportedException($"Unsupported connection type {Settings.Debug.ConnectionType}");
                }
            }

            Logger.Log(LogLevel.VVV, "main", $"Client Start: Waiting for Ready");
            // Wait until the server sent the ready packet
            _ReadyEvent.Wait(token);
            SendFilterList();

            Logger.Log(LogLevel.INF, "main", "Ready");
            IsReady = true;
        }

        public void Dispose() {
            if (!IsAlive) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClient Dispose called but not alive");
                return;
            }
            IsAlive = false;

            lock (StartStopLock) {
                if (Con == null)
                    return;
                Logger.Log(LogLevel.CRI, "main", "Shutdown");
                IsReady = false;

                HeartbeatTimer?.Dispose();
                Con?.Dispose();
                Con = null;

                Data.Dispose();
                _ReadyEvent?.Dispose();
            }
        }

        public void SendFilterList() {
            Logger.Log(LogLevel.INF, "main", "Sending filter list");
            Con?.Send(new DataNetFilterList {
                List = Data.DataTypeToSource.Values.Distinct().ToArray()
            });
        }


        public DataType Send(DataType data) {
            Con?.Send(data);
            return data;
        }

        public T Send<T>(T data) where T : DataType<T> {
            Con?.Send(data);
            return data;
        }

        public DataType SendAndHandle(DataType data) {
            Con?.Send(data);
            Data.Handle(null, data);
            return data;
        }

        public T SendAndHandle<T>(T data) where T : DataType<T> {
            Con?.Send(data);
            Data.Handle(null, data);
            return data;
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo info) {
            // The first DataPlayerInfo sent from the server is our own
            if (PlayerInfo == null || PlayerInfo.ID == info.ID) {
                PlayerInfo = info;

                if (Settings.NameKey == "Guest" && info.Name == "Guest") {
                    // strip off any '#x' suffix
                    int i = info.FullName.IndexOf('#');
                    string newName = i > 0 ? info.FullName.Substring(0, i) : info.FullName;

                    // take on the 'generated' Guest name
                    if (newName != "Guest") {
                        Logger.Log(LogLevel.INF, "playerinfo", $"Connected as Guest, but got '{newName}'. Saving fixed Guest name to config.");
                        Settings.Name = newName;
                    }
                }
            }
        }

        public bool Filter(CelesteNetConnection con, DataPlayerInfo info) {
            if (info != null && Options.AvatarsDisabled)
                info.DisplayName = info.FullName;
            return true;
        }

        public void Handle(CelesteNetConnection con, DataReady ready) {
            _ReadyEvent.Set();
        }

    }
}
