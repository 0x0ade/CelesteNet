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

        protected TcpListener TCPListener;
        protected UdpClient UDP;

        private Thread TCPListenerThread;
        private Thread UDPReadThread;

        private Dictionary<IPEndPoint, CelesteNetTCPUDPConnection> UDPMap = new Dictionary<IPEndPoint, CelesteNetTCPUDPConnection>();

        public TCPUDPServer(CelesteNetServer server) {
            Server = server;
            Server.Data.RegisterHandlersIn(this);
        }

        public void Start() {
            Logger.Log(LogLevel.CRI, "tcpudp", $"Startup on port {Server.Settings.MainPort}");

            TCPListener = new TcpListener(IPAddress.Any, Server.Settings.MainPort);
            TCPListener.Start();

            TCPListenerThread = new Thread(TCPListenerLoop) {
                Name = $"TCPUDPServer TCPListener ({GetHashCode()})",
                IsBackground = true
            };
            TCPListenerThread.Start();

            UDP = new UdpClient(Server.Settings.MainPort);

            UDPReadThread = new Thread(UDPReadLoop) {
                Name = $"TCPUDPServer UDPRead ({GetHashCode()})",
                IsBackground = true
            };
            UDPReadThread.Start();
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "tcpudp", "Shutdown");

            TCPListener.Stop();
            UDP.Close();
        }

        protected virtual void TCPListenerLoop() {
            try {
                while (Server.IsAlive) {
                    TcpClient client = TCPListener.AcceptTcpClient();

                    Logger.Log(LogLevel.VVV, "tcpudp", $"New TCP connection: {client.Client.RemoteEndPoint}");

                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 6000);

                    CelesteNetTCPUDPConnection con = new CelesteNetTCPUDPConnection(Server.Data, client, null);
                    con.Send(new DataTCPHTTPTeapot());
                    con.StartReadTCP();
                    Server.HandleConnect(con);
                }

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                Logger.Log(LogLevel.CRI, "tcpudp", $"Failed listening for TCP connection:\n{e}");
                Server.Dispose();
            }
        }

        protected virtual void UDPReadLoop() {
            try {
                using (MemoryStream stream = new MemoryStream())
                using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8)) {
                    while (Server.IsAlive) {
                        IPEndPoint remote = null;
                        byte[] raw = UDP.Receive(ref remote);
                        if (!UDPMap.TryGetValue(remote, out CelesteNetTCPUDPConnection con))
                            continue;

                        stream.Seek(0, SeekOrigin.Begin);
                        stream.Write(raw, 0, raw.Length);

                        stream.Seek(0, SeekOrigin.Begin);
                        Server.Data.Handle(con, Server.Data.Read(reader));
                    }
                }

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                Logger.Log(LogLevel.CRI, "tcpudp", $"Failed waiting for UDP data:\n{e}");
                Server.Dispose();
            }
        }


        #region Handlers

        public void Handle(CelesteNetTCPUDPConnection con, DataHandshakeTCPUDPClient handshake) {
            lock (Server.Connections)
                if (Server.PlayersByCon.ContainsKey(con))
                    return;

            IPEndPoint ep = (IPEndPoint) con.TCP.Client.RemoteEndPoint;
            con.UDP = UDP;
            con.UDPLocalEndPoint = (IPEndPoint) UDP.Client.LocalEndPoint;
            con.UDPRemoteEndPoint = new IPEndPoint(ep.Address, handshake.UDPPort);

            UDPMap[con.UDPRemoteEndPoint] = con;
            con.OnDisconnect += _ => UDPMap.Remove(con.UDPRemoteEndPoint);

            CelesteNetPlayerSession session = new CelesteNetPlayerSession(Server, con, Server.PlayerCounter++);
            session.Start(handshake);
        }

        #endregion

    }
}
