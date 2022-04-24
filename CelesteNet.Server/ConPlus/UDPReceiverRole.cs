using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public class UDPReceiverRole : MultipleSocketBinderRole {

        private class Worker : RoleWorker {

            public new UDPReceiverRole Role => (UDPReceiverRole) base.Role;

            public Worker(UDPReceiverRole role, NetPlusThread thread) : base(role, thread) { }

            protected override void StartWorker(Socket socket, CancellationToken token) {
                // Receive packets as long as the token isn't canceled
                // FIXME Definetly optimize this with recvmmsg (35%+ time wasted in socket calls)
                byte[] dgBuffer = new byte[Role.Server.Settings.UDPMaxDatagramSize];
                int dgSize;

                EnterActiveZone();
                token.Register(() => socket.Close());
                while (!token.IsCancellationRequested) {
                    EndPoint dgramSender = socket.LocalEndPoint!;
                    ExitActiveZone();
                    try {
                        dgSize = socket.ReceiveFrom(dgBuffer, ref dgramSender);
                    } catch (SocketException) {
                        // There's no better way to do this, as far as I know...
                        if (token.IsCancellationRequested)
                            return;
                        throw;
                    }
                    EnterActiveZone();
                    ConPlusTCPUDPConnection? con;

                    // Handle handshake messages
                    if (dgSize == 5 && dgBuffer[0] == 0xff) {
                        // Get the connection toke
                        uint conToken = BitConverter.ToUInt32(dgBuffer, 1);

                        // Get the connection from the token
                        if (!Role.conTokenMap.TryGetValue(conToken, out (ConPlusTCPUDPConnection con, EndPoint? udpEP) tokData))
                            continue;
                        con = tokData.con;

                        // Initialize the connection
                        lock (con.UDPLock) {
                            if (!con.UseUDP) {
                                con.Send(new DataLowLevelUDPInfo {
                                    ConnectionID = -1,
                                    MaxDatagramSize = 0
                                });
                                continue;
                            }

                            if (tokData.udpEP != null) {
                                // This makes hijacking possible, but is also
                                // how a client with changing IP addresses can
                                // reconnect it's UDP connection :(
                                Logger.Log(LogLevel.INF, "udprecv", $"Connection {con} UDP send endpoint changed: {tokData.udpEP} -> {dgramSender}");
                            }

                            // Initialize and establish the UDP connection
                            con.InitUDP(dgramSender, con.UDPNextConnectionID++, Role.Server.Settings.UDPMaxDatagramSize);

                            // Update the connection in the maps
                            Role.conTokenMap[conToken] = (con, dgramSender);
                            Role.endPointMap[dgramSender] = con;
                        }
                        continue;
                    }

                    // Get the associated connection
                    if (!Role.endPointMap.TryGetValue(dgramSender, out con) || con == null)
                        continue;

                    try {
                        // Handle the UDP datagram
                        con.HandleUDPDatagram(dgBuffer, dgSize);
                    } catch (Exception e) {
                        Role.Server.PacketDumper.DumpPacket(con, PacketDumper.TransportType.UDP, $"Exception while reading: {e}", dgBuffer, 0, dgSize);
                        Logger.Log(LogLevel.WRN, "udprecv", $"Error while reading from connection {con}: {e}");
                        con.DecreaseUDPScore(reason: "Error while reading from connection");
                    }
                }
                ExitActiveZone();
            }

        }

        private readonly ConcurrentDictionary<uint, (ConPlusTCPUDPConnection con, EndPoint? udpEP)> conTokenMap = new();
        private readonly ConcurrentDictionary<EndPoint, ConPlusTCPUDPConnection> endPointMap = new();
        private Socket? initialSock;

        public UDPReceiverRole(NetPlusThreadPool pool, CelesteNetServer server, EndPoint endPoint, Socket? initialSock = null) : base(pool, ProtocolType.Udp, endPoint) {
            Server = server;
            this.initialSock = initialSock;
        }

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

        protected override (Socket sock, bool ownsSocket) CreateSocket() {
            Socket? sock = Interlocked.Exchange(ref initialSock, null);
            if (sock != null)
                return (sock, false);
            return base.CreateSocket();
        }

        public void AddConnection(ConPlusTCPUDPConnection con) {
            conTokenMap.TryAdd(con.ConnectionToken, (con, null));
            con.OnUDPDeath += UDPDeath;
        }

        public void RemoveConnection(ConPlusTCPUDPConnection con) {
            con.OnUDPDeath -= UDPDeath;
            if (conTokenMap.TryRemove(con.ConnectionToken, out (ConPlusTCPUDPConnection con, EndPoint? udpEP) conData) && conData.udpEP != null)
                endPointMap.TryRemove(conData.udpEP, out _);
        }

        private void UDPDeath(CelesteNetTCPUDPConnection con, EndPoint ep) {
            endPointMap.TryRemove(ep, out _);
            if (conTokenMap.TryGetValue(con.ConnectionToken, out (ConPlusTCPUDPConnection con, EndPoint? udpEP) conData) && conData.udpEP != null)
                endPointMap.TryRemove(conData.udpEP, out _);
        }

        public CelesteNetServer Server { get; }

    }
}