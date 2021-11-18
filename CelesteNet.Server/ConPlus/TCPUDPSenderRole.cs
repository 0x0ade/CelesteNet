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
    - Popax21
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
                packetWriter = new CelesteNetBinaryWriter(role.Server.Data, null, packetStream);
                
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
                    CelesteNetTCPUDPConnection con = (CelesteNetTCPUDPConnection) queue.Con;
                    EnterActiveZone();
                    try {
                        switch (queueType) {
                            case QueueType.TCP: FlushTCPQueue(con, queue, token); break;
                            case QueueType.UDP: FlushUDPQueue(con, queue, token); break;
                        }
                    } catch (Exception e) {
                        // If the client closed the connection, just close the connection too
                        if (e is SocketException se && se.SocketErrorCode == SocketError.NotConnected) {
                            con.Dispose();
                            continue;
                        }

                        Logger.Log(LogLevel.WRN, "tcpudpsend", $"Error flushing connection {con} queue '{queue.Name}': {e}");
                        con.Dispose();
                    } finally {
                        ExitActiveZone();
                    }
                }
            }

            private void FlushTCPQueue(CelesteNetTCPUDPConnection con, CelesteNetSendQueue queue, CancellationToken token) {
                sockStream.Socket = con.TCPSocket;
                packetWriter.Strings = con.Strings;

                // Write all packets
                int byteCounter = 0, packetCounter = 0;
                foreach (DataType packet in queue.BackQueue) {
                    // Write the packet onto the temporary packet stream
                    packetStream.Position = 0;
                    if (packet is DataInternalBlob blob)
                        blob.Dump(packetWriter);
                    else
                        Role.Server.Data.Write(packetWriter, packet);
                    int packLen = (int) packetStream.Position;

                    // Write size and raw packet data into the actual stream
                    sockWriter.Write((UInt16) packLen);
                    sockStream.Write(packetStream.GetBuffer(), 0, packLen);
                    byteCounter += 2 + packLen;
                    packetCounter++;
                }

                // Iterate metrics
                using (tcpMetricsLock.W()) {
                    Role.Pool.IterateEventHeuristic(ref tcpByteRate, ref lastTcpByteRateUpdate, byteCounter, true);
                    Role.Pool.IterateEventHeuristic(ref tcpPacketRate, ref lastTcpPacketRateUpdate, packetCounter, true);
                }

                sockStream.Flush();
            }

            private void FlushUDPQueue(CelesteNetTCPUDPConnection con, CelesteNetSendQueue queue, CancellationToken token) {
                packetWriter.Strings = con.Strings;

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
                        // Send container and prepare new one
                        udpSocket.SendTo(udpBuffer, bufOff, SocketFlags.None, con.UDPEndpoint!);
                        udpBuffer[0] = con.NextUDPContainerID();
                        bufOff = 1;
                    }
                    Buffer.BlockCopy(packetStream.GetBuffer(), 0, udpBuffer, bufOff, packLen);
                    bufOff += packLen;

                    byteCounter += packLen;
                    packetCounter++;
                }
                if (bufOff > 1)
                    udpSocket.SendTo(udpBuffer, bufOff, SocketFlags.None, con.UDPEndpoint!);

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