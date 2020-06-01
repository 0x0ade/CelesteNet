using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Control;
using Mono.Options;
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
    public class TCPUDPServer : IDisposable {

        public readonly CelesteNetServer Server;

        protected TcpListener Listener;

        private Thread ListenerThread;

        public TCPUDPServer(CelesteNetServer server) {
            Server = server;
            Server.Data.RegisterHandlersIn(this);
        }

        public void Start() {
            Logger.Log(LogLevel.CRI, "tcpudp", $"Startup on port {Server.Settings.MainPort}");

            Listener = new TcpListener(IPAddress.Any, Server.Settings.MainPort);
            Listener.Start();

            ListenerThread = new Thread(ListenerThreadLoop) {
                Name = $"TCPUDPServer Listener ({GetHashCode()})",
                IsBackground = true
            };
            ListenerThread.Start();
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "tcpudp", "Shutdown");

            Listener.Stop();
        }

        protected virtual void ListenerThreadLoop() {
            try {
                while (Server.IsAlive) {
                    TcpClient client = Listener.AcceptTcpClient();
                    Logger.Log(LogLevel.VVV, "tcpudp", $"New TCP connection: {client.Client.RemoteEndPoint}");

                    Server.HandleConnect(new CelesteNetTCPUDPConnection(Server.Data, client, null));
                }

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                Logger.Log(LogLevel.CRI, "tcpudp", $"Failed listening:\n{e}");
                Server.Dispose();
            }
        }


        #region Handlers

        public void Handle(CelesteNetTCPUDPConnection con, DataHandshakeTCPUDPClient handshake) {
            if (Server.Sessions.ContainsKey(con))
                return;

            IPEndPoint ep = (IPEndPoint) con.TCP.Client.RemoteEndPoint;
            con.UDP = new UdpClient(new IPEndPoint(ep.Address, handshake.UDPPort));

            CelesteNetSession session = new CelesteNetSession(Server, con, Server.SessionCounter++);
            lock (Server.Sessions) {
                Server.Sessions[con] = session;
            }
            session.Start();
        }

        #endregion

    }
}
