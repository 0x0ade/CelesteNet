using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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

        public DataPlayer PlayerInfo;

        private ManualResetEvent HandshakeEvent = new ManualResetEvent(false);

        private object StartStopLock = new object();

        public CelesteNetClient()
            : this(new CelesteNetClientSettings()) {
        }

        public CelesteNetClient(CelesteNetClientSettings settings) {
            Settings = settings;

            Data = new DataContext();
            Data.RegisterHandlersIn(this);
        }

        public void Start() {
            lock (StartStopLock) {
                if (IsAlive)
                    return;

                Logger.Log(LogLevel.CRI, "main", $"Startup");
                IsAlive = true;

                switch (Settings.ConnectionType) {
                    case ConnectionType.Auto:
                    case ConnectionType.TCPUDP:
                        Logger.Log(LogLevel.INF, "main", "Connecting via TCP + UDP.");
                        CelesteNetTCPUDPConnection con = new CelesteNetTCPUDPConnection(Data, Settings.Host, Settings.Port);
                        con.OnDisconnect += _ => Dispose();
                        Con = con;
                        Logger.Log(LogLevel.INF, "main", $"Local endpoints: {con.TCP.Client.LocalEndPoint} / {con.UDP.Client.LocalEndPoint}");
                        con.Send(new DataHandshakeTCPUDPClient {
                            Name = Settings.Name,
                            UDPPort = ((IPEndPoint) con.UDP.Client.LocalEndPoint).Port
                        });
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported connection type {Settings.ConnectionType}");
                }

                Logger.Log(LogLevel.INF, "main", "Waiting for server handshake.");
                WaitHandle.WaitAny(new WaitHandle[] { HandshakeEvent });

                Logger.Log(LogLevel.INF, "main", "Ready");
                IsReady = true;
            }
        }

        public void Dispose() {
            lock (StartStopLock) {
                if (!IsAlive)
                    return;

                Logger.Log(LogLevel.CRI, "main", "Shutdown");
                IsAlive = false;
                IsReady = false;

                HandshakeEvent.Dispose();
                Con?.Dispose();
            }
        }


        #region Handlers

        public void Handle(CelesteNetConnection con, DataHandshakeServer handshake) {
            Logger.Log(LogLevel.INF, "main", $"Received handshake: {handshake}");
            if (handshake.Version != CelesteNetUtils.Version) {
                Dispose();
                throw new Exception($"Version mismatch - client {CelesteNetUtils.Version} vs server {handshake.Version}");
            }

            Data.Handle(con, handshake.PlayerInfo);

            HandshakeEvent.Set();
        }

        public void Handle(CelesteNetConnection con, DataPlayer info) {
            PlayerInfo = info;
        }

        #endregion

    }
}
