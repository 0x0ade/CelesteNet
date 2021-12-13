using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;
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

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClient : IDisposable {

        public readonly CelesteNetClientSettings Settings;

        public readonly DataContext Data;

        public CelesteNetConnection Con;
        public readonly IConnectionFeature[] ConFeatures;

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
        private ManualResetEventSlim _ReadyEvent;

        public DataPlayerInfo PlayerInfo;

        private readonly object StartStopLock = new();

        private System.Timers.Timer HeartbeatTimer;

        public CelesteNetClient()
            : this(new()) {
        }

        public CelesteNetClient(CelesteNetClientSettings settings) {
            Settings = settings;

            Data = new();
            Data.RegisterHandlersIn(this);

            _ReadyEvent = new(false);

            // Find connection features
            List<IConnectionFeature> conFeatures = new List<IConnectionFeature>();
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(IConnectionFeature).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                IConnectionFeature feature = (IConnectionFeature) Activator.CreateInstance(type);
                if (feature == null)
                    throw new Exception($"Cannot create instance of connection feature {type.FullName}");
                Logger.Log(LogLevel.VVV, "main", $"Found connection feature: {type.FullName}");
                conFeatures.Add(feature);
            }
            ConFeatures = conFeatures.ToArray();
        }

        public void Start(CancellationToken token) {
            if (IsAlive)
                return;
            IsAlive = true;

            lock (StartStopLock) {
                if (IsReady)
                    return;
                Logger.Log(LogLevel.CRI, "main", $"Startup");

                switch (Settings.ConnectionType) {
                    case ConnectionType.Auto:
                    case ConnectionType.TCPUDP:
                    case ConnectionType.TCP:
                        Logger.Log(LogLevel.INF, "main", $"Connecting via TCP/UDP to {Settings.Host}:{Settings.Port}");

                        // Create a TCP connection
                        Socket sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
                        try {
                            (uint conToken, IConnectionFeature[] conFeatures, CelesteNetTCPUDPConnection.Settings settings) teapotRes;
                            using (token.Register(() => sock.Close())) {
                                sock.ReceiveTimeout = sock.SendTimeout = 5000;
                                sock.Connect(Settings.Host, Settings.Port);

                                // Do the teapot handshake
                                teapotRes = Handshake.DoTeapotHandshake<CelesteNetClientTCPUDPConnection.Settings>(sock, ConFeatures, Settings.Name);
                                Logger.Log(LogLevel.INF, "main", $"Teapot handshake success: token {teapotRes.conToken} conFeatures '{teapotRes.conFeatures.Select(f => f.GetType().FullName).Aggregate((string) null, (a, f) => (a == null) ? f : $"{a}, {f}")}'");
                                sock.ReceiveTimeout = sock.SendTimeout = -1;
                            }

                            // Create a connection and start the heartbeat timer
                            CelesteNetClientTCPUDPConnection con = new CelesteNetClientTCPUDPConnection(Data, teapotRes.conToken, teapotRes.settings, sock);
                            con.OnDisconnect += _ => {
                                CelesteNetClientModule.Instance.Context?.Dispose();
                                Dispose();
                                CelesteNetClientModule.Instance.Context?.Status?.Set("Server disconnected", 3f);
                            };
                            if (Settings.ConnectionType == ConnectionType.TCP)
                                con.UseUDP = false;

                            // Initialize the heartbeat timer
                            heartbeatTimer = new(teapotRes.settings.HeartbeatInterval);
                            heartbeatTimer.AutoReset = true;
                            heartbeatTimer.Elapsed += (_, _) => {
                                string disposeReason = con.DoHeartbeatTick();
                                if (disposeReason != null) {
                                    Logger.Log(LogLevel.CRI, "main", disposeReason);
                                    CelesteNetClientContext ctx = CelesteNetClientModule.Instance.Context;
                                    Dispose();
                                }
                            };
                            heartbeatTimer.Start();

                            // Do the regular connection handshake
                            Handshake.DoConnectionHandshake(Con, teapotRes.conFeatures, token);
                            Logger.Log(LogLevel.INF, "main", $"Connection handshake success");

                            Con = con;
                        } catch (Exception) {
                            Con?.Dispose();
                            try {
                                sock.ShutdownSafe(SocketShutdown.Both);
                                sock.Close();
                            } catch (Exception) {}
                            sock.Dispose();
                            Con = null;
                            throw;
                        }

                        break;

                    default:
                        throw new NotSupportedException($"Unsupported connection type {Settings.ConnectionType}");
                }
            }

            SendFilterList();

            // Wait until the server sent the ready packet
            _ReadyEvent.Wait(token);

            Logger.Log(LogLevel.INF, "main", "Ready");
            IsReady = true;
        }

        public void Dispose() {
            if (!IsAlive)
                return;
            IsAlive = false;

            lock (StartStopLock) {
                if (Con == null)
                    return;
                Logger.Log(LogLevel.CRI, "main", "Shutdown");
                IsReady = false;

                heartbeatTimer?.Dispose();
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
            if (PlayerInfo == null || PlayerInfo.ID == info.ID)
                PlayerInfo = info;
        }

        public void Handle(CelesteNetConnection con, DataReady ready) {
            _ReadyEvent.Set();
        }

    }
}
