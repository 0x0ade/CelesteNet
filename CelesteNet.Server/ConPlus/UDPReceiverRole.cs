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
                // TODO We can optimize this with recvmmsg
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

                    if (dgSize == 5 && dgBuffer[0] == 0xff) {
                        // Get the connection token
                        uint conToken = BitConverter.ToUInt32(dgBuffer, 1);

                        // Get the connection from the token
                        if (!Role.conTokenMap.TryGetValue(conToken, out ConPlusTCPUDPConnection? tokCon) || tokCon == null)
                            continue;

                        // Initialize the connection
                        lock (tokCon.UDPLock) {
                            if (!tokCon.UseUDP) {
                                tokCon.Send(new DataLowLevelUDPInfo {
                                    ConnectionID = -1,
                                    MaxDatagramSize = 0
                                });
                                continue;
                            }

                            if (tokCon.UDPEndpoint != null) {
                                // This makes hijacking possible, but is also
                                // how a client with changing IP addresses can
                                // reconnect it's UDP connection :(
                                Logger.Log(LogLevel.INF, "udprecv", $"Connection {tokCon} UDP endpoint changed: {tokCon.UDPEndpoint} -> {dgramSender}");
                                Role.endPointMap.TryRemove(tokCon.UDPEndpoint, out _);
                            }

                            // Initialize and establish the UDP connection
                            tokCon.InitUDP(dgramSender, tokCon.UDPNextConnectionID++, Role.Server.Settings.UDPMaxDatagramSize);

                            // Add the connection to the map
                            Role.endPointMap[dgramSender] = tokCon;
                        }
                        continue;
                    }

                    // Get the associated connection
                    if (!Role.endPointMap.TryGetValue(dgramSender, out ConPlusTCPUDPConnection? con) || con == null)
                        continue;

                    try {
                        // Handle the UDP datagram
                        con.HandleUDPDatagram(dgBuffer, dgSize);
                    } catch (Exception e) {
                        Logger.Log(LogLevel.WRN, "udprecv", $"Error while reading from connection {con}: {e}");
                        con.DecreaseUDPScore();
                    }
                }
                ExitActiveZone();
            }

        }

        private readonly ConcurrentDictionary<uint, ConPlusTCPUDPConnection> conTokenMap = new();
        private readonly ConcurrentDictionary<EndPoint, ConPlusTCPUDPConnection> endPointMap = new();

        public UDPReceiverRole(NetPlusThreadPool pool, CelesteNetServer server, EndPoint endPoint) : base(pool, ProtocolType.Udp, endPoint) {
            Server = server;
        }

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

        public void AddConnection(ConPlusTCPUDPConnection con) {
            conTokenMap.TryAdd(con.ConnectionToken, con);
            con.OnUDPDeath += UDPDeath;
        }

        public void RemoveConnection(ConPlusTCPUDPConnection con) {
            con.OnUDPDeath -= UDPDeath;
            conTokenMap.TryRemove(con.ConnectionToken, out _);

            EndPoint? udpEP = con.UDPEndpoint;
            if (udpEP != null)
                endPointMap.TryRemove(udpEP!, out _);
        }

        private void UDPDeath(CelesteNetTCPUDPConnection con, EndPoint ep) {
            endPointMap.TryRemove(ep, out _);
        }

        public CelesteNetServer Server { get; }

    }
}