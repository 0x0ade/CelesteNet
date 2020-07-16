using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Options;
using System;
using System.Collections.Concurrent;
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

        protected TcpListener? TCPListener;
        protected UdpClient? UDP;

        private Thread? TCPListenerThread;
        private Thread? UDPReadThread;

        private uint UDPNextID = (uint) (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);
        private readonly ConcurrentDictionary<CelesteNetTCPUDPConnection, UDPPendingKey> UDPKeys = new ConcurrentDictionary<CelesteNetTCPUDPConnection, UDPPendingKey>();
        private readonly ConcurrentDictionary<UDPPendingKey, CelesteNetTCPUDPConnection> UDPPending = new ConcurrentDictionary<UDPPendingKey, CelesteNetTCPUDPConnection>();
        private readonly ConcurrentDictionary<IPEndPoint, CelesteNetTCPUDPConnection> UDPMap = new ConcurrentDictionary<IPEndPoint, CelesteNetTCPUDPConnection>();

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

            TCPListener?.Stop();
            UDP?.Close();

            Server.Data.UnregisterHandlersIn(this);
        }

        protected virtual void TCPListenerLoop() {
            try {
                while (Server.IsAlive && TCPListener != null) {
                    TcpClient client = TCPListener.AcceptTcpClient();

                    Logger.Log(LogLevel.VVV, "tcpudp", $"New TCP connection: {client.Client.RemoteEndPoint}");

                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 6000);
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true);

                    CelesteNetTCPUDPConnection con = new CelesteNetTCPUDPConnection(Server.Data, client, null);
                    uint token;
                    lock (UDPPending)
                        token = UDPNextID++;
                    UDPPendingKey key = new UDPPendingKey {
                        IPHash = ((IPEndPoint) client.Client.RemoteEndPoint).Address.GetHashCode(),
                        Token = token
                    };
                    UDPKeys[con] = key;
                    UDPPending[key] = con;
                    con.OnDisconnect += RemoveUDPPending;
                    con.Send(new DataTCPHTTPTeapot() {
                        ConnectionToken = token
                    });
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
                    while (Server.IsAlive && UDP != null) {
                        IPEndPoint? remote = null;
                        byte[] raw;
                        try {
                            raw = UDP.Receive(ref remote);
                        } catch (SocketException) {
                            continue;
                        }

                        if (remote == null)
                            continue;

                        if (!UDPMap.TryGetValue(remote, out CelesteNetTCPUDPConnection? con)) {
                            if (raw.Length == 4) {
                                UDPPendingKey key = new UDPPendingKey {
                                    IPHash = remote.Address.GetHashCode(),
                                    Token = BitConverter.ToUInt32(raw, 0)
                                };
                                if (UDPPending.TryRemove(key, out con)) {
                                    Logger.Log(LogLevel.CRI, "tcpudp", $"New UDP connection: {remote}");
                                    con.OnDisconnect -= RemoveUDPPending;

                                    con.UDP = UDP;
                                    con.UDPLocalEndPoint = (IPEndPoint) UDP.Client.LocalEndPoint;
                                    con.UDPRemoteEndPoint = remote;

                                    UDPMap[con.UDPRemoteEndPoint] = con;
                                    con.OnDisconnect += RemoveUDPMap;
                                    continue;
                                }
                            }

                            if (con == null)
                                continue;
                        }

                        try {
                            stream.Seek(0, SeekOrigin.Begin);
                            stream.Write(raw, 0, raw.Length);

                            stream.Seek(0, SeekOrigin.Begin);
                            Server.Data.Handle(con, Server.Data.Read(reader));
                        } catch (Exception e) {
                            Logger.Log(LogLevel.CRI, "tcpudp", $"Failed handling UDP data, {raw.Length} bytes:\n{con}\n{e}");
                            // Sometimes we receive garbage via UDP. Oh well...
                            Handle(con, new DataTCPOnlyDowngrade());
                        }
                    }
                }

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                Logger.Log(LogLevel.CRI, "tcpudp", $"Failed waiting for UDP data:\n{e}");
                Server.Dispose();
            }
        }

        private void RemoveUDPPending(CelesteNetConnection _con) {
            CelesteNetTCPUDPConnection con = (CelesteNetTCPUDPConnection) _con;
            if (UDPKeys.TryGetValue(con, out UDPPendingKey key)) {
                UDPPending.TryRemove(key, out _);
            }
        }

        private void RemoveUDPMap(CelesteNetConnection _con) {
            CelesteNetTCPUDPConnection con = (CelesteNetTCPUDPConnection) _con;
            UDPKeys.TryRemove(con, out _);
            IPEndPoint? ep = con.UDPRemoteEndPoint;
            if (ep != null)
                UDPMap.TryRemove(ep, out _);
        }


        #region Handlers

        public void Handle(CelesteNetTCPUDPConnection con, DataHandshakeTCPUDPClient handshake) {
            if (handshake.Version != CelesteNetUtils.Version) {
                con.Send(new DataDisconnectReason { Text = "Protocol version mismatch" });
                con.Send(new DataInternalDisconnect());
                return;
            }

            lock (Server.Connections)
                if (Server.PlayersByCon.ContainsKey(con))
                    return;

            CelesteNetPlayerSession session = new CelesteNetPlayerSession(Server, con, ++Server.PlayerCounter);
            session.Start(handshake);
        }

        public void Handle(CelesteNetTCPUDPConnection con, DataTCPOnlyDowngrade downgrade) {
            con.UDP = null;
            con.UDPLocalEndPoint = null;
            IPEndPoint? ep = con.UDPRemoteEndPoint;
            con.UDPRemoteEndPoint = null;

            if (UDPKeys.TryGetValue(con, out UDPPendingKey key))
                UDPPending[key] = con;

            if (ep != null)
                UDPMap.TryRemove(ep, out _);

            con.Send(downgrade);
        }

        #endregion

        private struct UDPPendingKey {
            public int IPHash;
            public uint Token;
        }

    }
}
