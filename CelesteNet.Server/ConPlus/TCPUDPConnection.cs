using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public class ConPlusTCPUDPConnection : CelesteNetTCPUDPConnection {

        public const int DownlinkCapCounterMax = 8;

        public class RateMetric : IDisposable {

            public readonly ConPlusTCPUDPConnection Con;
            private RWLock RateLock;
            private long LastByteRateUpdate, LastPacketRateUpdate;
            private float _ByteRate, _PacketRate;

            internal RateMetric(ConPlusTCPUDPConnection con) {
                Con = con;
                RateLock = new RWLock();
                LastByteRateUpdate = LastPacketRateUpdate = 0;
                _ByteRate = _PacketRate = 0;
            }

            public void Dispose() {
                RateLock.Dispose();
            }

            public void UpdateRate(int byteCount, int packetCount) {
                using (RateLock.W()) {
                    Con.Server.ThreadPool.IterateEventHeuristic(ref _ByteRate, ref LastByteRateUpdate, byteCount, true);
                    Con.Server.ThreadPool.IterateEventHeuristic(ref _PacketRate, ref LastPacketRateUpdate, packetCount, true);
                }
            }

            public float ByteRate {
                get {
                    using (RateLock.R())
                        return Con.Server.ThreadPool.IterateEventHeuristic(ref _ByteRate, ref LastByteRateUpdate);
                }
            }

            public float PacketRate {
                get {
                    using (RateLock.R())
                        return Con.Server.ThreadPool.IterateEventHeuristic(ref _PacketRate, ref LastPacketRateUpdate);
                }
            }

        }

        public readonly CelesteNetServer Server;
        public readonly TCPReceiverRole TCPReceiver;
        public readonly UDPReceiverRole UDPReceiver;
        public readonly TCPUDPSenderRole Sender;

        private RWLock UsageLock;
        private int DownlinkCapCounter = 0;
        private object TCPLock = new object();
        private byte[] TCPBuffer;
        private int TCPBufferOff;
        public int UDPNextConnectionID = 0;

        public readonly RateMetric TCPRecvRate, TCPSendRate;
        public readonly RateMetric UDPRecvRate, UDPSendRate;

        public ConPlusTCPUDPConnection(
            CelesteNetServer server,
            uint token,
            Settings settings,
            Socket tcpSock,
            TCPReceiverRole tcpReceiver,
            UDPReceiverRole udpReceiver,
            TCPUDPSenderRole sender
        ) : base(server.Data, token, settings, tcpSock, sender.TriggerTCPQueueFlush, sender.TriggerUDPQueueFlush) {
            Server = server;
            TCPReceiver = tcpReceiver;
            UDPReceiver = udpReceiver;
            Sender = sender;
            UsageLock = new RWLock();
            TCPRecvRate = new RateMetric(this);
            TCPSendRate = new RateMetric(this);
            UDPRecvRate = new RateMetric(this);
            UDPSendRate = new RateMetric(this);

            // Initialize TCP receiving
            tcpSock.Blocking = false;
            TCPBuffer = new byte[Math.Max(server.Settings.TCPBufferSize, 2+server.Settings.MaxPacketSize)];
            TCPBufferOff = 0;
            tcpReceiver.Poller.AddConnection(this);

            // Initialize UDP receiving
            udpReceiver.AddConnection(this);
        }

        // The usage lock could still be used after we dispose
        // So keep it alive as long as possible
        ~ConPlusTCPUDPConnection() => UsageLock.Dispose();

        protected override void Dispose(bool disposing) {
            TCPReceiver.Poller.RemoveConnection(this);
            UDPReceiver.RemoveConnection(this);
            using (UsageLock.W()) {
                base.Dispose(disposing);
                TCPRecvRate.Dispose();
                TCPSendRate.Dispose();
                UDPRecvRate.Dispose();
                UDPSendRate.Dispose();
            }
        }

        public IDisposable? Utilize(out bool alive) {
            if (!IsAlive) {
                alive = false;
                return null;
            }

            IDisposable dis = UsageLock.R();

            // Detect race conditions with Dispose
            if (!IsAlive) {
                dis.Dispose();
                alive = false;
                return null;
            }

            alive = true;
            return dis;
        }

        public override string? DoHeartbeatTick() {
            string? disposeReason = base.DoHeartbeatTick();
            if (disposeReason != null)
                return disposeReason;

            // Decrement the amount of times we hit the downlink cap
            if (Volatile.Read(ref DownlinkCapCounter) > 0)
                Interlocked.Decrement(ref DownlinkCapCounter);

            return null;
        }

        public void HandleTCPData() {
            lock (TCPLock) {
                using (Utilize(out bool alive)) {
                    if (!alive || !IsConnected)
                        return;

                    do {
                        // Receive data into the buffer
                        int numRead = TCPSocket.Receive(TCPBuffer, TCPBufferOff, TCPBuffer.Length - TCPBufferOff, SocketFlags.None);
                        if (numRead <= 0) {
                            // The remote closed the connection
                            Logger.Log(LogLevel.INF, "tcpudpcon", $"Remote of connection {this} closed the connection");
                            goto closeConnection;
                        }
                        TCPBufferOff += numRead;

                        // Let the connection know we got a heartbeat
                        TCPHeartbeat();

                        // Update metrics and check if we hit the cap
                        TCPRecvRate.UpdateRate(numRead, 0);
                        if (TCPRecvRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkBpTCap && Interlocked.Increment(ref DownlinkCapCounter) >= DownlinkCapCounterMax) {
                            Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit TCP downlink byte cap: {TCPRecvRate.ByteRate} BpS {Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkBpTCap} cap BpS");
                            goto closeConnection;
                        }

                        while (true) {
                            // Check if we have read the first two length bytes
                            if (2 <= TCPBufferOff) {
                                // Get the packet length
                                int packetLen = BitConverter.ToUInt16(TCPBuffer, 0);
                                if (packetLen < 0 || packetLen > Server.Settings.MaxPacketSize) {
                                    Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} sent packet over the size limit: {packetLen} > {Server.Settings.MaxPacketSize}");
                                    goto closeConnection;
                                }

                                // Did we receive the entire packet already?
                                if (2 + packetLen <= TCPBufferOff) {
                                    // Update metrics and check if we hit the cap
                                    TCPRecvRate.UpdateRate(0, 1);
                                    if (TCPRecvRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkPpTCap) {
                                        Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit TCP downlink packet cap: {TCPRecvRate.PacketRate} PpS {Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkPpTCap} cap PpS");
                                        goto closeConnection;
                                    }

                                    // Read the packet
                                    DataType packet;
                                    using (MemoryStream mStream = new MemoryStream(TCPBuffer, 2, packetLen))
                                    using (CelesteNetBinaryReader reader = new CelesteNetBinaryReader(Server.Data, Strings, SlimMap, mStream))
                                        packet = Server.Data.Read(reader);

                                    // Handle the packet
                                    switch (packet) {
                                        case DataLowLevelUDPInfo udpInfo: {
                                            HandleUDPInfo(udpInfo);
                                        } break;
                                        case DataLowLevelStringMap strMap: {
                                            Strings.RegisterWrite(strMap.String, strMap.ID);
                                        } break;
                                        case DataLowLevelSlimMap slimMap: {
                                            if (slimMap.PacketType != null)
                                                SlimMap.RegisterWrite(slimMap.PacketType, slimMap.ID);
                                        } break;
                                        default: {
                                            Receive(packet);
                                        } break;
                                    }

                                    // Remove the packet data from the buffer
                                    TCPBufferOff -= 2 + packetLen;
                                    Buffer.BlockCopy(TCPBuffer, 2 + packetLen, TCPBuffer, 0, TCPBufferOff);
                                    continue;
                                }
                            }
                            break;
                        }
                    } while (TCPSocket.Available > 0);

                    // Promote optimizations
                    PromoteOptimizations();
                }

                return;
                closeConnection:
                Dispose();
            }
        }

        public void HandleUDPDatagram(byte[] buffer, int dgSize) {
            lock (UDPLock) {
                using (Utilize(out bool alive)) {
                    if (!alive || !IsConnected)
                        return;

                    if (UDPEndpoint == null || dgSize <= 0)
                        return;

                    // Update metrics
                    UDPRecvRate.UpdateRate(dgSize, 0);
                    if (UDPRecvRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkBpTCap && Interlocked.Increment(ref DownlinkCapCounter) >= DownlinkCapCounterMax) {
                        Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit UDP downlink byte cap: {UDPRecvRate.ByteRate} BpS {Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkBpTCap} cap BpS");
                        goto closeConnection;
                    }

                    // Let the connection know we received a container
                    ReceivedUDPContainer(buffer[0]);

                    using (MemoryStream mStream = new MemoryStream(buffer, 0, dgSize))
                    using (CelesteNetBinaryReader reader = new CelesteNetBinaryReader(Server.Data, Strings, SlimMap, mStream)) {
                        // Get the container ID
                        byte containerID = reader.ReadByte();

                        // Read packets until we run out data
                        while (mStream.Position < dgSize-1) {
                            DataType packet = Server.Data.Read(reader);

                            // Update metrics
                            UDPRecvRate.UpdateRate(0, 1);
                            if (UDPRecvRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkPpTCap) {
                                Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit UDP downlink packet cap: {UDPRecvRate.PacketRate} PpS {Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkPpTCap} cap PpS");
                                goto closeConnection;
                            }

                            if (packet.TryGet<MetaOrderedUpdate>(Server.Data, out MetaOrderedUpdate? orderedUpdate))
                                orderedUpdate.UpdateID = containerID;
                            Receive(packet);
                        }
                    }

                    // Promote optimizations
                    PromoteOptimizations();
                }

                return;
                closeConnection:
                Dispose();
            }
        }

        public bool TCPSendCapped => TCPSendRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkBpTCap || TCPSendRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkPpTCap;
        public float TCPSendCapDelay => Math.Max(Server.Settings.HeuristicSampleWindow * Math.Max(1 - Server.Settings.PlayerTCPUplinkBpTCap / TCPSendRate.ByteRate, 1 - Server.Settings.PlayerTCPUplinkPpTCap / TCPSendRate.PacketRate), 0);
        public bool UDPSendCapped => UDPSendRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkBpTCap || UDPSendRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkPpTCap;

    }
}