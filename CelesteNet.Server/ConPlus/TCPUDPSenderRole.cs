using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Concurrent;
using System.IO;
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

        private enum SendAction {
            FlushTCPQueue, FlushTCPBuffer, FlushUDPQueue
        }

        private class Worker : RoleWorker {

            private readonly MemoryStream PacketStream;
            private readonly CelesteNetBinaryWriter PacketWriter;
            private readonly byte[] UDPBuffer;

            public Worker(TCPUDPSenderRole role, NetPlusThread thread) : base(role, thread) {
                PacketStream = new(role.Server.Settings.MaxPacketSize);
                PacketWriter = new(role.Server.Data, null, null, PacketStream);
                UDPBuffer = new byte[role.Server.Settings.UDPMaxDatagramSize];
            }

            public override void Dispose() {
                base.Dispose();
                PacketStream.Dispose();
                PacketWriter.Dispose();
            }

            protected internal override void StartWorker(CancellationToken token) {
                // Handle queues from the queue
                foreach ((SendAction act, ConPlusTCPUDPConnection con) in Role.QueueQueue.GetConsumingEnumerable(token)) {
                    EnterActiveZone();
                    try {
                        using (con.Utilize(out bool alive)) {
                            // Maybe the connection got closed while it was in the queue
                            if (!alive || !con.IsConnected)
                                continue;
                            try {
                                switch (act) {
                                    case SendAction.FlushTCPQueue: {
                                        con.FlushTCPSendQueue();
                                    } break;
                                    case SendAction.FlushTCPBuffer: {
                                        con.FlushTCPSendBuffer();
                                    } break;
                                    case SendAction.FlushUDPQueue: {
                                        FlushUDPSendQueue(con, token);
                                    } break;
                                }
                            } catch (Exception e) {
                                switch (act) {
                                    case SendAction.FlushTCPQueue:
                                    case SendAction.FlushTCPBuffer: {
                                        if (e is SocketException se && se.IsDisconnect()) {
                                            Logger.Log(LogLevel.INF, "tcpsend", $"Remote of connection {con} closed the connection");
                                            con.DisposeSafe();
                                            continue;
                                        }

                                        Logger.Log(LogLevel.WRN, "tcpsend", $"Error flushing connection {con} TCP data: {e}");
                                        con.DisposeSafe();
                                    } break;
                                    case SendAction.FlushUDPQueue: {
                                        Logger.Log(LogLevel.WRN, "udpsend", $"Error flushing connection {con} UDP queue: {e}");
                                        con.UDPQueue.SignalFlushed();
                                        con.DecreaseUDPScore(reason: "Error flushing queue");
                                    } break;
                                }
                            }
                        }
                    } finally {
                        ExitActiveZone();
                    }
                }
            }

            private void FlushUDPSendQueue(ConPlusTCPUDPConnection con, CancellationToken token) {
                int byteCounter = 0, packetCounter = 0;
                lock (con.UDPLock) {
                    // TODO This could be optimized with sendmmsg

                    // If there's no established UDP connection, just drop all packets
                    if (con.UDPEndpoint is not EndPoint remoteEP) {
                        con.UDPQueue.SignalFlushed();
                        return;
                    }

                    // Check if the connection's capped
                    if (con.UDPSendCapped) {
                        Logger.Log(LogLevel.WRN, "udpsend", $"Connection {con} hit UDP uplink cap: {con.UDPSendRate.ByteRate} BpS {con.UDPSendRate.PacketRate} PpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerUDPUplinkBpTCap} cap BpS {con.Server.CurrentTickRate * con.Server.Settings.PlayerUDPUplinkPpTCap} cap PpS");

                        // UDP's unreliable, just drop the excess packets
                        con.UDPQueue.SignalFlushed();
                        return;
                    }

                    try {
                        PacketWriter.Strings = con.Strings;
                        PacketWriter.CoreTypeMap = con.CoreTypeMap;

                        // Write all packets
                        UDPBuffer[0] = con.NextUDPContainerID();
                        int bufOff = 1;

                        foreach (DataType packet in con.UDPQueue.BackQueue) {
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
                                con.UDPSendRate.UpdateMetric(bufOff, 1);
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
                            con.UDPSendRate.UpdateMetric(bufOff, 1);
                        }

                        // Signal the queue that it's flushed
                        con.UDPQueue.SignalFlushed();
                    } finally {
                        PacketWriter.Strings = null;
                        PacketWriter.CoreTypeMap = null;
                    }
                }

                // Iterate metrics
                Role.UpdateUDPStats(byteCounter, packetCounter);
            }

            public new TCPUDPSenderRole Role => (TCPUDPSenderRole) base.Role;

        }

        private readonly RWLock TCPMetricsLock;
        private long LastTCPByteRateUpdate, LastTCPPacketRateUpdate;
        private float _TCPByteRate, _TCPPacketRate;

        private readonly RWLock UDPMetricsLock;
        private long LastUDPByteRateUpdate, LastUDPPacketRateUpdate;
        private float _UDPByteRate, _UDPPacketRate;

        public override int MinThreads => 1;
        public override int MaxThreads => int.MaxValue;

        public CelesteNetServer Server { get; }
        public Socket UDPSocket { get; }

        public float TCPByteRate {
            get {
                using (TCPMetricsLock.R())
                    return Pool.IterateEventHeuristic(ref _TCPByteRate, ref LastTCPByteRateUpdate);
            }
        }

        public float TCPPacketRate {
            get {
                using (TCPMetricsLock.R())
                    return Pool.IterateEventHeuristic(ref _TCPPacketRate, ref LastTCPPacketRateUpdate);
            }
        }

        public float UDPByteRate {
            get {
                using (UDPMetricsLock.R())
                    return Pool.IterateEventHeuristic(ref _UDPByteRate, ref LastUDPByteRateUpdate);
            }
        }

        public float UDPPacketRate {
            get {
                using (UDPMetricsLock.R())
                    return Pool.IterateEventHeuristic(ref _UDPPacketRate, ref LastUDPPacketRateUpdate);
            }
        }

        private readonly BlockingCollection<(SendAction, ConPlusTCPUDPConnection)> QueueQueue = new();

        public TCPUDPSenderRole(NetPlusThreadPool pool, CelesteNetServer server, EndPoint udpEP) : base(pool) {
            Server = server;
            TCPMetricsLock = new();
            UDPMetricsLock = new();
            UDPSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            UDPSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            UDPSocket.ExclusiveAddressUse = false;
            UDPSocket.Bind(udpEP);
        }

        public override void Dispose() {
            UDPSocket.Dispose();
            QueueQueue.Dispose();
            TCPMetricsLock.Dispose();
            UDPMetricsLock.Dispose();
            base.Dispose();
        }

        public override RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

        public void TriggerTCPQueueFlush(ConPlusTCPUDPConnection con) => QueueQueue.Add((SendAction.FlushTCPQueue, con));
        public void TriggerTCPBufferFlush(ConPlusTCPUDPConnection con) => QueueQueue.Add((SendAction.FlushTCPBuffer, con));
        public void TriggerUDPQueueFlush(ConPlusTCPUDPConnection con) => QueueQueue.Add((SendAction.FlushUDPQueue, con));

        internal void UpdateTCPStats(int numBytes, int numPackets) {
            using (TCPMetricsLock.W()) {
                Pool.IterateEventHeuristic(ref _TCPByteRate, ref LastTCPByteRateUpdate, numBytes, true);
                Pool.IterateEventHeuristic(ref _TCPPacketRate, ref LastTCPPacketRateUpdate, numPackets, true);
            }
        }

        internal void UpdateUDPStats(int numBytes, int numPackets) {
            using (UDPMetricsLock.W()) {
                Pool.IterateEventHeuristic(ref _UDPByteRate, ref LastUDPByteRateUpdate, numBytes, true);
                Pool.IterateEventHeuristic(ref _UDPPacketRate, ref LastUDPPacketRateUpdate, numPackets, true);
            }
        }

    }
}