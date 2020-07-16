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

        private readonly ManualResetEvent HandshakeEvent = new ManualResetEvent(false);
        private DataType HandshakeClient;

        private readonly object StartStopLock = new object();

        private int UDPDeathScore;

        public CelesteNetClient()
            : this(new CelesteNetClientSettings()) {
        }

        public CelesteNetClient(CelesteNetClientSettings settings) {
            Settings = settings;

            Data = new DataContext();
            Data.RegisterHandlersIn(this);
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

                        CelesteNetTCPUDPConnection con = new CelesteNetTCPUDPConnection(Data, Settings.Host, Settings.Port, Settings.ConnectionType != ConnectionType.TCP);
                        con.SendUDP = false;
                        con.OnDisconnect += _ => Dispose();
                        con.OnUDPError += OnUDPError;
                        Con = con;
                        Logger.Log(LogLevel.INF, "main", $"Local endpoints: {con.TCP.Client.LocalEndPoint} / {con.UDP?.Client.LocalEndPoint?.ToString() ?? "null"}");

                        // Server replies with a dummy HTTP response to filter out dumb port sniffers.
                        uint token = con.ReadTeapot();

                        con.SendKeepAlive = true;
                        con.StartReadTCP();
                        con.StartReadUDP();

                        HandshakeClient = new DataHandshakeTCPUDPClient {
                            Name = Settings.Name,
                            ConnectionToken = token
                        };

                        con.Send(HandshakeClient);
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported connection type {Settings.ConnectionType}");
                }
            }

            Logger.Log(LogLevel.INF, "main", "Waiting for server handshake.");
            WaitHandle.WaitAny(new WaitHandle[] { HandshakeEvent });

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

                HandshakeEvent.Set();
                HandshakeEvent.Dispose();
                Con?.Dispose();
                Con = null;
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

        private void OnUDPError(CelesteNetTCPUDPConnection con, Exception e, bool read) {
            if (!read)
                return;

            if (UDPDeathScore < 50) {
                UDPDeathScore += 10;
                Logger.Log(LogLevel.CRI, "main", $"UDP connection died. Oh well.\nUDP death score:{UDPDeathScore}\n{this}\n{(e is ObjectDisposedException ? "Disposed" : e is SocketException ? e.Message : e.ToString())}");
                con.SendUDP = false;
                con.Send(new DataTCPOnlyDowngrade());

            } else {
                Logger.Log(LogLevel.CRI, "main", $"UDP connection died too often. Switching to TCP only.\nUDP death score:{UDPDeathScore}\n{this}\n{(e is ObjectDisposedException ? "Disposed" : e is SocketException ? e.Message : e.ToString())}");
            }
        }


        #region Handlers

        public bool Filter(CelesteNetConnection con, DataType data) {
            if ((data.DataFlags & DataFlags.Update) == DataFlags.Update) {
                if (con is CelesteNetTCPUDPConnection tcpudp) {
                    tcpudp.SendUDP = true;
                    if (UDPDeathScore > -10)
                        UDPDeathScore -= 1;
                }
            }

            return true;
        }

        public void Handle(CelesteNetConnection con, DataHandshakeServer handshake) {
            Logger.Log(LogLevel.INF, "main", $"Received handshake: {handshake}");
            if (handshake.Version != CelesteNetUtils.Version) {
                Dispose();
                throw new Exception($"Version mismatch - client {CelesteNetUtils.Version} vs server {handshake.Version}");
            }

            // Needed because while the server knows the client's TCP endpoint, the UDP endpoint is ambiguous.
            if (con is CelesteNetTCPUDPConnection && HandshakeClient is DataHandshakeTCPUDPClient hsClient) {
                hsClient.IsUDP = true;
                con.Send(new DataUDPConnectionToken {
                    Value = hsClient.ConnectionToken
                });
            }

            Data.Handle(con, handshake.PlayerInfo);

            HandshakeEvent.Set();
        }

        public void Handle(CelesteNetTCPUDPConnection con, DataTCPOnlyDowngrade downgrade) {
            if (HandshakeClient is DataHandshakeTCPUDPClient hsClient) {
                con.StartReadUDP();
                con.Send(new DataUDPConnectionToken {
                    Value = hsClient.ConnectionToken
                });
            }
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo info) {
            if (PlayerInfo == null || PlayerInfo.ID == info.ID)
                PlayerInfo = info;
        }

        #endregion

    }
}
