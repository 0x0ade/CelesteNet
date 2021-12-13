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

            private BufferedSocketStream SockStream;
            private BinaryWriter SockWriter;
            private MemoryStream PacketStream;
            private CelesteNetBinaryWriter PacketWriter;

            private Socket UDPSocket;
            private byte[] UDPBuffer;

            private RWLock TCPMetricsLock;
            private long LastTCPByteRateUpdate, LastTCPPacketRateUpdate;
            private float _TCPByteRate, _TCPPacketRate;

            private RWLock UDPMetricsLock;
            private long LastUDPByteRateUpdate, LastUDPPacketRateUpdate;
            private float _UDPByteRate, _UDPPacketRate;

            public Worker(TCPUDPSenderRole role, NetPlusThread thread) : base(role, thread) {
                SockStream = new BufferedSocketStream(role.Server.Settings.TCPBufferSize);
                SockWriter = new BinaryWriter(SockStream);
                PacketStream = new MemoryStream(role.Server.Settings.MaxPacketSize);
                PacketWriter = new CelesteNetBinaryWriter(role.Server.Data, null, null, PacketStream);

                UDPSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
                UDPSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                UDPSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 0);
                UDPSocket.EnableEndpointReuse();
                UDPSocket.Bind(role.UDPEndPoint);
                UDPBuffer = new byte[role.Server.Settings.UDPMaxDatagramSize];

                TCPMetricsLock = new RWLock();
                UDPMetricsLock = new RWLock();
            }

            public override void Dispose() {
                base.Dispose();
                TCPMetricsLock.Dispose();
                UDPMetricsLock.Dispose();
                SockStream.Dispose();
                SockWriter.Dispose();
                PacketStream.Dispose();
                PacketWriter.Dispose();
                UDPSocket.Dispose();
            }

            protected internal override void StartWorker(CancellationToken token) {
                // Handle queues from the queue
                foreach ((QueueType queueType, CelesteNetSendQueue queue) in Role.QueueQueue.GetConsumingEnumerable(token)) {
                    ConPlusTCPUDPConnection con = (ConPlusTCPUDPConnection) queue.Con;
                    EnterActiveZone();
                    try {
                        using (con.Utilize(out bool alive)) {
                            // Maybe the connection got closed while it was in the queue
                            if (!alive || !con.IsConnected)
                                continue;

                            switch (queueType) {
                                case QueueType.TCP: {
                                    FlushTCPQueue(con, queue, token);
                                } break;
                                case QueueType.UDP: {
                                    lock (con.UDPLock) {
                                        // If there's no established UDP connection, just drop all packets
                                        if (con.UDPEndpoint == null)
                                            queue.SignalFlushed();
                                        else
                                            FlushUDPQueue(con, queue, token);
                                    }
                                } break;
                            }
                        }
                    } catch (Exception e) {
                        switch (queueType) {
                            case QueueType.TCP: {
                                if (e is SocketException se && se.IsDisconnect()) {
                                    Logger.Log(LogLevel.INF, "tcpsend", $"Remote of connection {con} closed the connection");
                                    con.DisposeSafe();
                                    continue;
                                }

                                Logger.Log(LogLevel.WRN, "tcpsend", $"Error flushing connection {con} TCP queue '{queue.Name}': {e}");
                                con.DisposeSafe();
                            } break;
                            case QueueType.UDP: {
                                Logger.Log(LogLevel.DBG, "udpsend", $"Error flushing connection {con} UDP queue '{queue.Name}': {e}");
                                con.DecreaseUDPScore();
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
                    queue.DelayFlush(con.TCPSendCapDelay, true);
                    return;
                }

                SockStream.Socket = con.TCPSocket;
                PacketWriter.Strings = con.Strings;
                PacketWriter.SlimMap = con.SlimMap;

                // Write all packets
                int byteCounter = 0, packetCounter = 0;
                while (queue.BackQueue.TryDequeue(out DataType? packet)) {
                    // Write the packet onto the temporary packet stream
                    PacketStream.Position = 0;
                    con.Data.Write(PacketWriter, packet!);
                    PacketWriter.Flush();
                    int packLen = (int) PacketStream.Position;

                    // Write size and raw packet data into the actual stream
                    SockWriter.Write((UInt16) packLen);
                    SockStream.Write(PacketStream.GetBuffer(), 0, packLen);

                    // Update connection metrics and check if we hit the connection cap
                    con.TCPSendRate.UpdateRate(2 + packLen, 1);
                    if (con.TCPSendCapped)
                        break;

                    byteCounter += 2 + packLen;
                    packetCounter++;

                    if (!(packet is DataLowLevelKeepAlive))
                        con.SurpressTCPKeepAlives();
                }
                SockStream.Flush();
                SockStream.Socket = null;

                // Signal the queue that it's flushed if we didn't hit a cap
                // Else requeue it to be flushed again later
                if (queue.BackQueue.Count <= 0)
                    queue.SignalFlushed();
                else
                    queue.DelayFlush(con.TCPSendCapDelay, true);

                // Iterate metrics
                using (TCPMetricsLock.W()) {
                    Role.Pool.IterateEventHeuristic(ref _TCPByteRate, ref LastTCPByteRateUpdate, byteCounter, true);
                    Role.Pool.IterateEventHeuristic(ref _TCPPacketRate, ref LastTCPPacketRateUpdate, packetCounter, true);
                }
            }

            private void FlushUDPQueue(ConPlusTCPUDPConnection con, CelesteNetSendQueue queue, CancellationToken token) {
                // TODO This could be optimized with sendmmsg

                // Check if the connection's capped
                if (con.UDPSendCapped) {
                    Logger.Log(LogLevel.WRN, "udpsend", $"Connection {con} hit UDP uplink cap: {con.UDPSendRate.ByteRate} BpS {con.UDPSendRate.PacketRate} PpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerUDPUplinkBpTCap} cap BpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerUDPUplinkPpTCap} cap PpS");

                    // UDP's unreliable, just drop the excess packets
                    queue.SignalFlushed();
                    return;
                }

                PacketWriter.Strings = con.Strings;
                PacketWriter.SlimMap = con.SlimMap;

                // Write all packets
                UDPBuffer[0] = con.NextUDPContainerID();
                int bufOff = 1;

                int byteCounter = 0, packetCounter = 0;
                foreach (DataType packet in queue.BackQueue) {
                    // Write the packet onto the temporary packet stream
                    PacketStream.Position = 0;
                    con.Data.Write(PacketWriter, packet);
                    PacketWriter.Flush();
                    int packLen = (int) PacketStream.Position;

                    // Copy packet data to the container buffer
                    if (bufOff + packLen > UDPBuffer.Length) {
                        // Send container & start a new one
                        UDPSocket.SendTo(UDPBuffer, bufOff, SocketFlags.None, con.UDPEndpoint!);
                        UDPBuffer[0] = con.NextUDPContainerID();
                        bufOff = 1;

                        // Update connection metrics and check if we hit the connection cap
                        con.UDPSendRate.UpdateRate(bufOff, 1);
                        if (con.UDPSendCapped)
                            break;
                    }

                    Buffer.BlockCopy(PacketStream.GetBuffer(), 0, UDPBuffer, bufOff, packLen);
                    bufOff += packLen;

                    byteCounter += packLen;
                    packetCounter++;

                    if (!(packet is DataLowLevelKeepAlive))
                        con.SurpressUDPKeepAlives();
                }

                // Send the last container
                if (bufOff > 1) {
                    UDPSocket.SendTo(UDPBuffer, bufOff, SocketFlags.None, con.UDPEndpoint!);
                    con.UDPSendRate.UpdateRate(bufOff, 1);
                }

                // Signal the queue that it's flushed
                queue.SignalFlushed();

                // Iterate metrics
                using (UDPMetricsLock.W()) {
                    Role.Pool.IterateEventHeuristic(ref _UDPByteRate, ref LastTCPByteRateUpdate, byteCounter, true);
                    Role.Pool.IterateEventHeuristic(ref _UDPPacketRate, ref LastTCPPacketRateUpdate, packetCounter, true);
                }
            }

            public new TCPUDPSenderRole Role => (TCPUDPSenderRole) base.Role;

            public float TCPByteRate {
                get {
                    using (TCPMetricsLock.R())
                        return Role.Pool.IterateEventHeuristic(ref _TCPByteRate, ref LastTCPByteRateUpdate);
                }
            }

            public float TCPPacketRate {
                get {
                    using (TCPMetricsLock.R())
                        return Role.Pool.IterateEventHeuristic(ref _TCPPacketRate, ref LastTCPPacketRateUpdate);
                }
            }

            public float UDPByteRate {
                get {
                    using (UDPMetricsLock.R())
                        return Role.Pool.IterateEventHeuristic(ref _UDPByteRate, ref LastUDPByteRateUpdate);
                }
            }

            public float UDPPacketRate {
                get {
                    using (UDPMetricsLock.R())
                        return Role.Pool.IterateEventHeuristic(ref _UDPPacketRate, ref LastUDPPacketRateUpdate);
                }
            }

        }

        private BlockingCollection<(QueueType, CelesteNetSendQueue)> QueueQueue;

        public TCPUDPSenderRole(NetPlusThreadPool pool, CelesteNetServer server, EndPoint udpEP) : base(pool) {
            Server = server;
            UDPEndPoint = udpEP;
            QueueQueue = new BlockingCollection<(QueueType, CelesteNetSendQueue)>();
        }

        public override void Dispose() {
            QueueQueue.Dispose();
            base.Dispose();
        }

        public override RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

        public void TriggerTCPQueueFlush(CelesteNetSendQueue queue) => QueueQueue.Add((QueueType.TCP, queue));
        public void TriggerUDPQueueFlush(CelesteNetSendQueue queue) => QueueQueue.Add((QueueType.UDP, queue));

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