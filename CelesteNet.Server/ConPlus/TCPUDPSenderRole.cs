using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    /*
    The TCP/UDP sender role gets specified as the queue flusher for the TCP/UDP
    send queues. When a queue needs to be flushed, it get's added to a queue of
    all waiting queues (queueception), which workers pull from to actually send
    packets. It also does some fancy buffering.
    -Popax21
    */
    public class TCPUDPSenderRole : NetPlusThreadRole {

        private enum QueueType {
            TCP, UDP
        }

        private class Worker : RoleWorker {

            private BufferedSocketStream sockStream;
            private BinaryWriter sockWriter;
            private MemoryStream packetStream;
            private CelesteNetBinaryWriter packetWriter;

            private Socket udpSocket;
            private byte[] udpBuffer;

            private RWLock tcpMetricsLock;
            private long lastTcpByteRateUpdate, lastTcpPacketRateUpdate;
            private float tcpByteRate, tcpPacketRate;

            private RWLock udpMetricsLock;
            private long lastUdpByteRateUpdate, lastUdpPacketRateUpdate;
            private float udpByteRate, udpPacketRate;

            public Worker(TCPUDPSenderRole role, NetPlusThread thread) : base(role, thread) {
                sockStream = new BufferedSocketStream(role.Server.Settings.TCPBufferSize);
                sockWriter = new BinaryWriter(sockStream);
                packetStream = new MemoryStream(role.Server.Settings.MaxPacketSize);
                packetWriter = new CelesteNetBinaryWriter(role.Server.Data, null, null, packetStream);
                
                udpSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
                udpSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                udpSocket.EnableEndpointReuse();
                udpSocket.Bind(role.UDPEndPoint);
                udpBuffer = new byte[role.Server.Settings.UDPMaxDatagramSize];

                tcpMetricsLock = new RWLock();
                udpMetricsLock = new RWLock();
            }

            public override void Dispose() {
                base.Dispose();
                tcpMetricsLock.Dispose();
                udpMetricsLock.Dispose();
                sockStream.Dispose();
                sockWriter.Dispose();
                packetStream.Dispose();
                packetWriter.Dispose();
                udpSocket.Dispose();
            }

            protected internal override void StartWorker(CancellationToken token) {
                // Handle queues from the queue
                foreach ((QueueType queueType, CelesteNetSendQueue queue) in Role.queueQueue.GetConsumingEnumerable(token)) {
                    ConPlusTCPUDPConnection con = (ConPlusTCPUDPConnection) queue.Con;
                    EnterActiveZone();
                    try {
                        // Maybe the connection got closed while it was in the queue
                        if (!con.IsConnected)
                            continue;

                        switch (queueType) {
                            case QueueType.TCP: {
                                try {
                                    FlushTCPQueue(con, queue, token);
                                } catch (Exception e) {
                                    if (e is SocketException se && se.IsDisconnect()) {
                                        con.Dispose();
                                        continue;
                                    }

                                    Logger.Log(LogLevel.WRN, "tcpsend", $"Error flushing connection {con} TCP queue '{queue.Name}': {e}");
                                    con.Dispose();
                                }
                            } break;
                            case QueueType.UDP: {
                                try {
                                    lock (con.UDPLock) {
                                        // If there's no established UDP connection, just drop all packets
                                        if (con.UDPEndpoint == null)
                                            queue.SignalFlushed();
                                        else
                                            FlushUDPQueue(con, queue, token);
                                    }
                                } catch (Exception e) {
                                    Logger.Log(LogLevel.DBG, "udpsend", $"Error flushing connection {con} UDP queue '{queue.Name}': {e}");
                                    con.DecreaseUDPScore();
                                }
                            } break;
                        }
                    } finally {
                        ExitActiveZone();
                    }
                }
            }

            private void FlushTCPQueue(ConPlusTCPUDPConnection con, CelesteNetSendQueue queue, CancellationToken token) {
                // Check if the connection's capped
                if (con.TCPSendCapped) {
                    Logger.Log(LogLevel.WRN, "tcpsend", $"Connection {con} hit TCP uplink cap: {con.TCPSendRate.ByteRate} BpS {con.TCPSendRate.PacketRate} PpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerTCPUplinkBpTCap} cap BpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerTCPUplinkPpTCap} cap PpS");
                    
                    // Requeue the queue to be flushed later
                    queue.DelayFlush(con.TCPSendCapDelay);
                    return;
                }

                sockStream.Socket = con.TCPSocket;
                packetWriter.Strings = con.Strings;
                packetWriter.SlimMap = con.SlimMap;

                // Write all packets
                int byteCounter = 0, packetCounter = 0;
                while (queue.BackQueue.TryDequeue(out DataType? packet)) {
                    // Write the packet onto the temporary packet stream
                    packetStream.Position = 0;
                    if (packet is DataInternalBlob blob)
                        blob.Dump(packetWriter);
                    else
                        Role.Server.Data.Write(packetWriter, packet!);
                    int packLen = (int) packetStream.Position;

                    // Write size and raw packet data into the actual stream
                    sockWriter.Write((UInt16) packLen);
                    sockStream.Write(packetStream.GetBuffer(), 0, packLen);
                    byteCounter += 2 + packLen;
                    packetCounter++;

                    // Update connection metrics and check if we hit the connection cap
                    con.TCPSendRate.UpdateRate(2 + packLen, 1);
                    if (con.TCPSendCapped)
                        break;

                    // Surpress keep alives
                    con.SurpressTCPKeepAlives();
                }
                sockStream.Flush();
                sockStream.Socket = null;

                // Signal the queue that it's flushed if we didn't hit a cap
                // Else requeue it to be flushed again later
                if (queue.BackQueue.Count <= 0)
                    queue.SignalFlushed();
                else
                    queue.DelayFlush(con.TCPSendCapDelay);

                // Iterate metrics
                using (tcpMetricsLock.W()) {
                    Role.Pool.IterateEventHeuristic(ref tcpByteRate, ref lastTcpByteRateUpdate, byteCounter, true);
                    Role.Pool.IterateEventHeuristic(ref tcpPacketRate, ref lastTcpPacketRateUpdate, packetCounter, true);
                }
            }

            private void FlushUDPQueue(ConPlusTCPUDPConnection con, CelesteNetSendQueue queue, CancellationToken token) {
                // Check if the connection's capped
                if (con.UDPSendCapped) {
                    Logger.Log(LogLevel.WRN, "udpsend", $"Connection {con} hit UDP uplink cap: {con.UDPSendRate.ByteRate} BpS {con.UDPSendRate.PacketRate} PpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerUDPUplinkBpTCap} cap BpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerUDPUplinkPpTCap} cap PpS");
                    
                    // UDP's unreliable, just drop the excess packets
                    queue.SignalFlushed();
                    return;
                }

                packetWriter.Strings = con.Strings;
                packetWriter.SlimMap = con.SlimMap;

                // Write all packets
                udpBuffer[0] = con.NextUDPContainerID();
                int bufOff = 1;

                int byteCounter = 0, packetCounter = 0;
                foreach (DataType packet in queue.BackQueue) {
                    // Write the packet onto the temporary packet stream
                    packetStream.Position = 0;
                    if (packet is DataInternalBlob blob)
                        blob.Dump(packetWriter);
                    else
                        Role.Server.Data.Write(packetWriter, packet);
                    int packLen = (int) packetStream.Position;

                    // Copy packet data to the container buffer
                    if (bufOff + packLen > udpBuffer.Length) {
                        // Send container
                        udpSocket.SendTo(udpBuffer, bufOff, SocketFlags.None, con.UDPEndpoint!);
                        bufOff = 1;
                        
                        // Update connection metrics and check if we hit the connection cap
                        con.UDPSendRate.UpdateRate(bufOff, 1);
                        if (con.UDPSendCapped)
                            break;

                        // Start a new container
                        udpBuffer[0] = con.NextUDPContainerID();
                    }

                    Buffer.BlockCopy(packetStream.GetBuffer(), 0, udpBuffer, bufOff, packLen);
                    bufOff += packLen;

                    byteCounter += packLen;
                    packetCounter++;

                    // Surpress keep alives
                    con.SurpressUDPKeepAlives();
                }

                // Send the last container
                if (bufOff > 1) {
                    udpSocket.SendTo(udpBuffer, bufOff, SocketFlags.None, con.UDPEndpoint!);
                    con.UDPSendRate.UpdateRate(bufOff, 1);
                }

                // Signal the queue that it's flushed
                queue.SignalFlushed();

                // Iterate metrics
                using (udpMetricsLock.W()) {
                    Role.Pool.IterateEventHeuristic(ref udpByteRate, ref lastTcpByteRateUpdate, byteCounter, true);
                    Role.Pool.IterateEventHeuristic(ref udpPacketRate, ref lastTcpPacketRateUpdate, packetCounter, true);
                }
            }

            public new TCPUDPSenderRole Role => (TCPUDPSenderRole) base.Role;

            public float TCPByteRate {
                get {
                    using (tcpMetricsLock.R())
                        return Role.Pool.IterateEventHeuristic(ref tcpByteRate, ref lastTcpByteRateUpdate, 0);
                }
            }

            public float TCPPacketRate {
                get {
                    using (tcpMetricsLock.R())
                        return Role.Pool.IterateEventHeuristic(ref tcpPacketRate, ref lastTcpPacketRateUpdate, 0);
                }
            }

            public float UDPByteRate {
                get {
                    using (udpMetricsLock.R())
                        return Role.Pool.IterateEventHeuristic(ref udpByteRate, ref lastUdpByteRateUpdate, 0);
                }
            }

            public float UDPPacketRate {
                get {
                    using (udpMetricsLock.R())
                        return Role.Pool.IterateEventHeuristic(ref udpPacketRate, ref lastUdpPacketRateUpdate, 0);
                }
            }

        }

        private BlockingCollection<(QueueType, CelesteNetSendQueue)> queueQueue;

        public TCPUDPSenderRole(NetPlusThreadPool pool, CelesteNetServer server, EndPoint udpEP) : base(pool) {
            Server = server;
            UDPEndPoint = udpEP;
            queueQueue = new BlockingCollection<(QueueType, CelesteNetSendQueue)>();
        }

        public override void Dispose() {
            queueQueue.Dispose();
            base.Dispose();
        }

        public override RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);
        
        public void TriggerTCPQueueFlush(CelesteNetSendQueue queue) => queueQueue.Add((QueueType.TCP, queue));
        public void TriggerUDPQueueFlush(CelesteNetSendQueue queue) => queueQueue.Add((QueueType.UDP, queue));

        public override int MinThreads => 1;
        public override int MaxThreads => int.MaxValue;

        public CelesteNetServer Server { get; }
        public EndPoint UDPEndPoint { get; }

        public float TCPByteRate => EnumerateWorkers().Aggregate(0f, (r, w) => r + ((Worker) w).TCPByteRate);
        public float TCPPacketRate => EnumerateWorkers().Aggregate(0f, (r, w) => r + ((Worker) w).TCPPacketRate);
        
        public float UDPByteRate => EnumerateWorkers().Aggregate(0f, (r, w) => r + ((Worker) w).UDPByteRate);
        public float UDPPacketRate => EnumerateWorkers().Aggregate(0f, (r, w) => r + ((Worker) w).UDPPacketRate);

    }
}