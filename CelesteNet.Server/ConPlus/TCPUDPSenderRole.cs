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

            private readonly BufferedSocketStream SockStream;
            private readonly BinaryWriter SockWriter;
            private readonly MemoryStream PacketStream;
            private readonly CelesteNetBinaryWriter PacketWriter;
            private readonly byte[] UDPBuffer;

            private readonly RWLock TCPMetricsLock;
            private long LastTCPByteRateUpdate, LastTCPPacketRateUpdate;
            private float _TCPByteRate, _TCPPacketRate;

            private readonly RWLock UDPMetricsLock;
            private long LastUDPByteRateUpdate, LastUDPPacketRateUpdate;
            private float _UDPByteRate, _UDPPacketRate;

            public Worker(TCPUDPSenderRole role, NetPlusThread thread) : base(role, thread) {
                SockStream = new(role.Server.Settings.TCPBufferSize);
                SockWriter = new(SockStream);
                PacketStream = new(role.Server.Settings.MaxPacketSize);
                PacketWriter = new(role.Server.Data, null, null, PacketStream);
                UDPBuffer = new byte[role.Server.Settings.UDPMaxDatagramSize];

                TCPMetricsLock = new();
                UDPMetricsLock = new();
            }

            public override void Dispose() {
                base.Dispose();
                TCPMetricsLock.Dispose();
                UDPMetricsLock.Dispose();
                SockStream.Dispose();
                SockWriter.Dispose();
                PacketStream.Dispose();
                PacketWriter.Dispose();
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
                                    FlushUDPQueue(con, queue, token);
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
                                Logger.Log(LogLevel.WRN, "udpsend", $"Error flushing connection {con} UDP queue '{queue.Name}': {e}");
                                con.DecreaseUDPScore(reason: "Error flushing queue");
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

                int byteCounter = 0, packetCounter = 0;
                try {
                    SockStream.Socket = con.TCPSocket;
                    PacketWriter.Strings = con.Strings;
                    PacketWriter.CoreTypeMap = con.CoreTypeMap;

                    // Write all packets
                    while (queue.BackQueue.TryDequeue(out DataType? packet)) {
                        // Write the packet onto the temporary packet stream
                        PacketStream.Position = 0;
                        con.Data.Write(PacketWriter, packet);
                        PacketWriter.Flush();
                        int packLen = (int) PacketStream.Position;

                        // Write size and raw packet data into the actual stream
                        SockWriter.Write((ushort) packLen);
                        SockWriter.Flush();
                        SockStream.Write(PacketStream.GetBuffer(), 0, packLen);

                        // Update connection metrics and check if we hit the connection cap
                        con.TCPSendRate.UpdateRate(2 + packLen, 1);
                        if (con.TCPSendCapped)
                            break;

                        byteCounter += 2 + packLen;
                        packetCounter++;

                        if (packet is not DataLowLevelKeepAlive)
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
                } finally {
                    SockStream.Socket = null;
                    PacketWriter.Strings = null;
                    PacketWriter.CoreTypeMap = null;
                }

                // Iterate metrics
                using (TCPMetricsLock.W()) {
                    Role.Pool.IterateEventHeuristic(ref _TCPByteRate, ref LastTCPByteRateUpdate, byteCounter, true);
                    Role.Pool.IterateEventHeuristic(ref _TCPPacketRate, ref LastTCPPacketRateUpdate, packetCounter, true);
                }
            }

            private void FlushUDPQueue(ConPlusTCPUDPConnection con, CelesteNetSendQueue queue, CancellationToken token) {
                int byteCounter = 0, packetCounter = 0;
                lock (con.UDPLock) {
                    // TODO This could be optimized with sendmmsg

                    // If there's no established UDP connection, just drop all packets
                    if (con.UDPEndpoint is not EndPoint remoteEP) {
                        queue.SignalFlushed();
                        return;
                    }

                    // Check if the connection's capped
                    if (con.UDPSendCapped) {
                        Logger.Log(LogLevel.WRN, "udpsend", $"Connection {con} hit UDP uplink cap: {con.UDPSendRate.ByteRate} BpS {con.UDPSendRate.PacketRate} PpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerUDPUplinkBpTCap} cap BpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerUDPUplinkPpTCap} cap PpS");

                        // UDP's unreliable, just drop the excess packets
                        queue.SignalFlushed();
                        return;
                    }

                    try {
                        PacketWriter.Strings = con.Strings;
                        PacketWriter.CoreTypeMap = con.CoreTypeMap;

                        // Write all packets
                        UDPBuffer[0] = con.NextUDPContainerID();
                        int bufOff = 1;

                        foreach (DataType packet in queue.BackQueue) {
                            // Write the packet onto the temporary packet stream
                            PacketStream.Position = 0;
                            con.Data.Write(PacketWriter, packet);
                            PacketWriter.Flush();
                            int packLen = (int) PacketStream.Position;

                            // Copy packet data to the container buffer
                            if (bufOff + packLen > UDPBuffer.Length) {
                                // Send container & start a new one
                                Role.UDPSocket.SendTo(UDPBuffer, bufOff, SocketFlags.None, remoteEP);
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

                            if (packet is not DataLowLevelKeepAlive)
                                con.SurpressUDPKeepAlives();
                        }

                        // Send the last container
                        if (bufOff > 1) {
                            Role.UDPSocket.SendTo(UDPBuffer, bufOff, SocketFlags.None, remoteEP);
                            con.UDPSendRate.UpdateRate(bufOff, 1);
                        }

                        // Signal the queue that it's flushed
                        queue.SignalFlushed();
                    } finally {
                        PacketWriter.Strings = null;
                        PacketWriter.CoreTypeMap = null;
                    }
                }

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

        public override int MinThreads => 1;
        public override int MaxThreads => int.MaxValue;

        public CelesteNetServer Server { get; }
        public Socket UDPSocket { get; }

        public float TCPByteRate => EnumerateWorkers().Aggregate(0f, (r, w) => r + ((Worker) w).TCPByteRate);
        public float TCPPacketRate => EnumerateWorkers().Aggregate(0f, (r, w) => r + ((Worker) w).TCPPacketRate);

        public float UDPByteRate => EnumerateWorkers().Aggregate(0f, (r, w) => r + ((Worker) w).UDPByteRate);
        public float UDPPacketRate => EnumerateWorkers().Aggregate(0f, (r, w) => r + ((Worker) w).UDPPacketRate);

        private readonly BlockingCollection<(QueueType, CelesteNetSendQueue)> QueueQueue = new();

        public TCPUDPSenderRole(NetPlusThreadPool pool, CelesteNetServer server, EndPoint udpEP) : base(pool) {
            Server = server;
            UDPSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            UDPSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            UDPSocket.EnableEndpointReuse();
            UDPSocket.Bind(udpEP);
        }

        public override void Dispose() {
            UDPSocket.Dispose();
            QueueQueue.Dispose();
            base.Dispose();
        }

        public override RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

        public void TriggerTCPQueueFlush(CelesteNetSendQueue queue) => QueueQueue.Add((QueueType.TCP, queue));
        public void TriggerUDPQueueFlush(CelesteNetSendQueue queue) => QueueQueue.Add((QueueType.UDP, queue));

    }
}