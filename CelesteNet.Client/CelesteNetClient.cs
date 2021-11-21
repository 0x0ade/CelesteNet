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

        public DataPlayerInfo PlayerInfo;

        private readonly object StartStopLock = new();

        private const int UDPDeathScoreMin = -1;
        private const int UDPDeathScoreMax = 5;
        private const int UDPAliveScoreMax = 100;

        public CelesteNetClient()
            : this(new()) {
        }

        public CelesteNetClient(CelesteNetClientSettings settings) {
            Settings = settings;

            Data = new();
            Data.RegisterHandlersIn(this);

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

        public void Start() {
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
                            sock.Connect(Settings.Host, Settings.Port);

                            // Do the teapot handshake
                            (int conToken, IConnectionFeature[] features, int maxPacketSize, float mergeWindow, float heartbeatInterval) = Handshake.DoTeapotHandshake(sock, ConFeatures, Settings.Name);
                            Logger.Log(LogLevel.INF, "main", $"Teapot handshake success: token {conToken} conFeatures '{features.Select(f => f.GetType().FullName).Aggregate((string) null, (a, f) => (a == null) ? f : $"{a}, {f}")}' maxPacketSize {maxPacketSize} mergeWindow {mergeWindow} heartbeatInterval {heartbeatInterval}");

                            // Create a connection and do the regular handshake
                            Con = new CelesteNetClientTCPUDPConnection(Data, conToken, maxPacketSize, mergeWindow, sock);
                            Con.OnDisconnect += _ => Dispose();
                            Handshake.DoConnectionHandshake(Con, features);
                            Logger.Log(LogLevel.INF, "main", $"Connection handshake success");
                        } catch (Exception) {
                            Con?.Dispose();
                            try {
                                sock.Shutdown(SocketShutdown.Both);
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

                Con?.Dispose();
                Con = null;

                Data.Dispose();
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

        #region Handlers

        public void Handle(CelesteNetConnection con, DataPlayerInfo info) {
            if (PlayerInfo != null && PlayerInfo.ID == info.ID)
                PlayerInfo = info;
        }

        #endregion

    }
}
