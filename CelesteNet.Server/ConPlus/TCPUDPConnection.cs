using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Server {
    public class ConPlusTCPUDPConnection : CelesteNetTCPUDPConnection {

        public const int DownlinkCapCounterMax = 8;

        public class RateMetric : IDisposable {

            public readonly ConPlusTCPUDPConnection Con;
            private readonly RWLock MetricLock;
            private long LastByteRateUpdate, LastPacketRateUpdate;
            private float _ByteRate, _PacketRate;

            public float ByteRate {
                get {
                    using (MetricLock.R())
                        return Con.Server.ThreadPool.IterateEventHeuristic(ref _ByteRate, ref LastByteRateUpdate);
                }
            }

            public float PacketRate {
                get {
                    using (MetricLock.R())
                        return Con.Server.ThreadPool.IterateEventHeuristic(ref _PacketRate, ref LastPacketRateUpdate);
                }
            }

            internal RateMetric(ConPlusTCPUDPConnection con) {
                Con = con;
                MetricLock = new();
                LastByteRateUpdate = LastPacketRateUpdate = 0;
                _ByteRate = _PacketRate = 0;
            }

            public void Dispose() {
                MetricLock.Dispose();
            }

            public void UpdateMetric(int byteCount, int packetCount) {
                using (MetricLock.W()) {
                    Con.Server.ThreadPool.IterateEventHeuristic(ref _ByteRate, ref LastByteRateUpdate, byteCount, true);
                    Con.Server.ThreadPool.IterateEventHeuristic(ref _PacketRate, ref LastPacketRateUpdate, packetCount, true);
                }
            }

        }

        public readonly CelesteNetServer Server;
        public readonly TCPReceiverRole TCPReceiver;
        public readonly UDPReceiverRole UDPReceiver;
        public readonly TCPUDPSenderRole Sender;

        private RWLock? UsageLock;
        private int UsageLockCounter;
        private int DownlinkCapCounter = 0;
        private readonly object TCPLock = new();
        private readonly byte[] TCPRecvBuffer, TCPSendBuffer;
        private readonly MemoryStream TCPSendBufferStream;
        private readonly CelesteNetBinaryWriter TCPSendPacketWriter;
        private volatile bool TCPTriggerSendBufferFlush, TCPDisconnectAfterSendBufferFlush;
        private volatile float TCPSendQueueDelay;
        private int TCPRecvBufferOff, TCPSendBufferOff, TCPSendBufferNumBytes, TCPSendBufferNumPackets, TCPSendBufferRetries;
        public int UDPNextConnectionID = 0;

        public readonly RateMetric TCPRecvRate, TCPSendRate;
        public readonly RateMetric UDPRecvRate, UDPSendRate;

        private int _TCPPingMs, _UDPPingMs;
        public int TCPPingMs => _TCPPingMs;
        public int? UDPPingMs {
            get {
                lock (UDPLock)
                    return (UDPEndpoint != null && UDPConnectionID >= 0) ? _UDPPingMs : null;
            }
        }

        public bool TCPSendCapped => TCPSendRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkBpTCap || TCPSendRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkPpTCap;
        public float TCPSendCapDelay => Math.Max(Server.Settings.HeuristicSampleWindow * Math.Max(1 - Server.Settings.PlayerTCPUplinkBpTCap / TCPSendRate.ByteRate, 1 - Server.Settings.PlayerTCPUplinkPpTCap / TCPSendRate.PacketRate), 0);
        public bool UDPSendCapped => UDPSendRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkBpTCap || UDPSendRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkPpTCap;

        public ConPlusTCPUDPConnection(
            CelesteNetServer server,
            uint token,
            Settings settings,
            Socket tcpSock,
            TCPReceiverRole tcpReceiver,
            UDPReceiverRole udpReceiver,
            TCPUDPSenderRole sender
        ) : base(server.Data, token, settings, tcpSock) {
            Server = server;
            TCPReceiver = tcpReceiver;
            UDPReceiver = udpReceiver;
            Sender = sender;
            using ((UsageLock = new()).W()) {
                UsageLockCounter = 1;

                TCPRecvRate = new(this);
                TCPSendRate = new(this);
                UDPRecvRate = new(this);
                UDPSendRate = new(this);

                // Initialize TCP receiving
                tcpSock.Blocking = false;
                TCPRecvBuffer = new byte[Math.Max(server.Settings.TCPRecvBufferSize, 2 + server.Settings.MaxPacketSize)];
                TCPRecvBufferOff = 0;
                tcpReceiver.Poller.AddConnection(this);

                // Initialize TCP sending
                TCPSendBuffer = new byte[(2 + server.Settings.MaxPacketSize) * server.Settings.MaxQueueSize];
                TCPSendBufferStream = new(TCPSendBuffer);
                TCPSendPacketWriter = new(server.Data, Strings, CoreTypeMap, TCPSendBufferStream);
                TCPSendBufferOff = TCPSendBufferNumBytes = TCPSendBufferNumPackets = 0;

                // Initialize UDP receiving
                OnUDPDeath += (_, _) => _UDPPingMs = 0;
                udpReceiver.AddConnection(this);
            }
        }

        protected override void Dispose(bool disposing) {
            TCPReceiver?.Poller?.RemoveConnection(this);
            UDPReceiver?.RemoveConnection(this);

            // Usage lock can't be null here, as UsageLockCounter is not zero
            using (UsageLock!.W()) {
                base.Dispose(disposing);
                TCPSendPacketWriter?.Dispose();
                TCPSendBufferStream?.Dispose();
                TCPRecvRate?.Dispose();
                TCPSendRate?.Dispose();
                UDPRecvRate?.Dispose();
                UDPSendRate?.Dispose();
            }

            // Decrement the usage counter and possibly dispose the lock
            if (Interlocked.Decrement(ref UsageLockCounter) <= 0)
                Interlocked.Exchange(ref UsageLock, null)?.Dispose();
        }

        public override void DisposeSafe() {
            if (!IsAlive || SafeDisposeTriggered)
                return;
            SafeDisposeTriggered = true;
            Server.SafeDisposeQueue.Add(this);
        }

        protected override void FlushTCPQueue() => Sender.TriggerTCPQueueFlush(this);
        protected override void FlushUDPQueue() => Sender.TriggerUDPQueueFlush(this);

        public IDisposable? Utilize(out bool alive) {
            // Aquire the lock
            Interlocked.Increment(ref UsageLockCounter);
            IDisposable? dis = IsAlive ? UsageLock?.R() : null;

            // Detect race conditions with Dispose
            if (dis == null || !IsAlive) {
                dis?.Dispose();

                // We might have kept the usage lock alive
                if (Interlocked.Decrement(ref UsageLockCounter) <= 0)
                    Interlocked.Exchange(ref UsageLock, null)?.Dispose();

                alive = false;
                return null;
            }

            // The connection is still alive, and can't die while we hold the lock
            // So the following condition should never be true
            if (Interlocked.Decrement(ref UsageLockCounter) <= 0)
                throw new InvalidOperationException("Usage lock counter reached zero while connection is alive. THIS SHOULD NEVER HAPPEN!");

            alive = true;
            return dis;
        }

        public override string? DoHeartbeatTick() {
            string? disposeReason = base.DoHeartbeatTick();
            if (disposeReason != null || !IsConnected)
                return disposeReason;

            // Decrement the amount of times we hit the downlink cap
            if (Volatile.Read(ref DownlinkCapCounter) > 0)
                Interlocked.Decrement(ref DownlinkCapCounter);

            // Potentially trigger a send buffer flush
            if (TCPTriggerSendBufferFlush) {
                TCPTriggerSendBufferFlush = false;
                Sender.TriggerTCPBufferFlush(this);
            }

            return null;
        }

        public void SendPingRequest() {
            // Send ping request packet
            DataLowLevelPingRequest pingRequest = new DataLowLevelPingRequest() {
                PingTime = Server.ThreadPool.RuntimeWatch.ElapsedMilliseconds
            };

            TCPQueue.Enqueue(pingRequest);

            lock (UDPLock) {
                if (UDPEndpoint != null && UDPConnectionID >= 0)
                    UDPQueue.Enqueue(pingRequest);
            }
        }

        public void HandleTCPData() {
            lock (TCPLock) {
                using (Utilize(out bool alive)) {
                    if (!alive || !IsConnected)
                        return;

                    try {
                        do {
                            // Receive data into the buffer
                            int numRead = TCPSocket.Receive(TCPRecvBuffer, TCPRecvBufferOff, TCPRecvBuffer.Length - TCPRecvBufferOff, SocketFlags.None);
                            if (numRead <= 0) {
                                // The remote closed the connection
                                Logger.Log(LogLevel.INF, "tcpudpcon", $"Remote of connection {this} closed the connection");
                                DisposeSafe();
                                return;
                            }
                            TCPRecvBufferOff += numRead;

                            // Let the connection know we got a heartbeat
                            TCPHeartbeat();

                            // Update metrics and check if we hit the cap
                            TCPRecvRate.UpdateMetric(numRead, 0);
                            if (TCPRecvRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkBpTCap && Interlocked.Increment(ref DownlinkCapCounter) >= DownlinkCapCounterMax) {
                                Server.PacketDumper.DumpPacket(this, PacketDumper.TransportType.TCP, "Downlink byte cap hit", TCPRecvBuffer, 0, TCPRecvBufferOff);
                                Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit TCP downlink byte cap: {TCPRecvRate.ByteRate} BpS {Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkBpTCap} cap BpS");
                                DisposeSafe();
                                return;
                            }

                            while (true) {
                                // Check if we have read the first two length bytes
                                if (2 <= TCPRecvBufferOff) {
                                    // Get the packet length
                                    int packetLen = BitConverter.ToUInt16(TCPRecvBuffer, 0);
                                    if (packetLen < 0 || packetLen > Server.Settings.MaxPacketSize) {
                                        Server.PacketDumper.DumpPacket(this, PacketDumper.TransportType.TCP, "Packet over size limit", TCPRecvBuffer, 0, TCPRecvBufferOff);
                                        Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} sent packet over the size limit: {packetLen} > {Server.Settings.MaxPacketSize}");
                                        DisposeSafe();
                                        return;
                                    }

                                    // Did we receive the entire packet already?
                                    if (2 + packetLen <= TCPRecvBufferOff) {
                                        // Update metrics and check if we hit the cap
                                        TCPRecvRate.UpdateMetric(0, 1);
                                        if (TCPRecvRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkPpTCap && Interlocked.Increment(ref DownlinkCapCounter) >= DownlinkCapCounterMax) {
                                            Server.PacketDumper.DumpPacket(this, PacketDumper.TransportType.TCP, "Downlink packet cap hit", TCPRecvBuffer, 0, TCPRecvBufferOff);
                                            Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit TCP downlink packet cap: {TCPRecvRate.PacketRate} PpS {Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkPpTCap} cap PpS");
                                            DisposeSafe();
                                            return;
                                        }

                                        // Read the packet
                                        DataType packet;
                                        using (MemoryStream mStream = new(TCPRecvBuffer, 2, packetLen))
                                        using (CelesteNetBinaryReader reader = new(Server.Data, Strings, CoreTypeMap, mStream)) {
                                            packet = Server.Data.Read(reader);
                                            if (mStream.Position != packetLen) {
                                                Server.PacketDumper.DumpPacket(this, PacketDumper.TransportType.TCP, "Downlink packet cap hit", TCPRecvBuffer, 0, TCPRecvBufferOff);
                                                Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} didn't read all data in TCP vdgram: {mStream.Position} read, {packetLen} total");
                                                DisposeSafe();
                                                return;
                                            }
                                        }

                                        // Handle the packet
                                        switch (packet) {
                                            case DataLowLevelPingReply pingReply: {
                                                _TCPPingMs = (int) (Server.ThreadPool.RuntimeWatch.ElapsedMilliseconds - pingReply.PingTime);
                                            } break;
                                            case DataLowLevelUDPInfo udpInfo: {
                                                HandleUDPInfo(udpInfo);
                                            } break;
                                            case DataLowLevelStringMap strMap: {
                                                Strings.RegisterWrite(strMap.String, strMap.ID);
                                            } break;
                                            case DataLowLevelCoreTypeMap coreTypeMap: {
                                                if (coreTypeMap.PacketType != null)
                                                    CoreTypeMap.RegisterWrite(coreTypeMap.PacketType, coreTypeMap.ID);
                                            } break;
                                            default: {
                                                Receive(packet);
                                            } break;
                                        }

                                        // Remove the packet data from the buffer
                                        TCPRecvBufferOff -= 2 + packetLen;
                                        Buffer.BlockCopy(TCPRecvBuffer, 2 + packetLen, TCPRecvBuffer, 0, TCPRecvBufferOff);
                                        continue;
                                    }
                                }
                                break;
                            }
                        } while (TCPSocket.Available > 0);

                        // Promote optimizations
                        PromoteOptimizations();
                    } catch (Exception e) {
                        if (e is SocketException se && se.IsDisconnect()) {
                            Logger.Log(LogLevel.INF, "tcprecv", $"Remote of connection {this} closed the connection");
                            DisposeSafe();
                        } else {
                            Server.PacketDumper.DumpPacket(this, PacketDumper.TransportType.TCP, $"Exception while reading: {e}", TCPRecvBuffer, 0, TCPRecvBufferOff);
                            Logger.Log(LogLevel.WRN, "tcprecv", $"Error while reading from connection {this}: {e}");
                            DisposeSafe();
                        }
                    }
                }
            }
        }

        public void FlushTCPSendQueue() {
            using (Utilize(out bool alive)) {
                if (!alive || !IsConnected || TCPQueue.BackQueue.Count <= 0)
                    return;

                if (TCPSendBufferNumBytes > 0 || TCPSendBufferNumPackets > 0)
                    throw new InvalidOperationException("Can't flush the TCP queue when the send buffer wasn't flushed!");

                // Check if the connection's capped
                if (TCPSendCapped) {
                    Logger.Log(LogLevel.WRN, "tcpsend", $"Connection {this} hit TCP uplink cap: {TCPSendRate.ByteRate} BpS {TCPSendRate.PacketRate} PpS {Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkBpTCap} cap BpS {Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkPpTCap} cap PpS");

                    // Requeue the queue to be flushed later
                    TCPQueue.DelayFlush(TCPSendCapDelay, true);
                    return;
                }

                // Write packets to the buffer
                TCPTriggerSendBufferFlush = false;
                TCPSendBufferOff = 0;
                TCPSendBufferStream.Position = 0;
                while (TCPQueue.BackQueue.TryDequeue(out DataType? packet)) {
                    // Check for special packets
                    if (packet is DataInternalDisconnect) {
                        TCPDisconnectAfterSendBufferFlush = true;
                        break;
                    }

                    // Write the packet
                    long origPos = TCPSendBufferStream.Position;
                    TCPSendBufferStream.Position = origPos + 2;
                    Data.Write(TCPSendPacketWriter, packet);
                    TCPSendPacketWriter.Flush();
                    ushort packLen = (ushort) (TCPSendBufferStream.Position - origPos - 2);

                    // Write size prefix
                    TCPSendBufferStream.Position = origPos;
                    TCPSendPacketWriter.Write(packLen);
                    TCPSendPacketWriter.Flush();
                    TCPSendBufferStream.Position = origPos + 2 + packLen;

                    // Update metadata
                    TCPSendBufferNumBytes += 2 + packLen;
                    TCPSendBufferNumPackets++;

                    if (packet is not DataLowLevelKeepAlive)
                        SurpressTCPKeepAlives();

                    // Update connection metrics and check if we hit the connection cap
                    TCPSendRate.UpdateMetric(2 + packLen, 1);
                    if (TCPSendCapped)
                        break;
                }

                // Set the queue delay
                if (TCPQueue.BackQueue.Count <= 0)
                    TCPSendQueueDelay = -1f;
                else
                    TCPSendQueueDelay = TCPSendCapDelay;

                // Initial send buffer flush
                FlushTCPSendBuffer();
            }
        }

        public void FlushTCPSendBuffer() {
            using (Utilize(out bool alive)) {
                if (!alive || !IsConnected)
                    return;

                try {
                    // Try to flush the buffer
                    while (TCPSendBufferOff < TCPSendBufferNumBytes)
                        TCPSendBufferOff += TCPSocket.Send(TCPSendBuffer, TCPSendBufferOff, TCPSendBufferNumBytes - TCPSendBufferOff, SocketFlags.None);
                    Sender.UpdateTCPStats(TCPSendBufferNumBytes, TCPSendBufferNumPackets);

                    // Signal the queue that it's flushed if we didn't hit a cap
                    // Else requeue it to be flushed again later
                    if (TCPSendQueueDelay <= 0)
                        TCPQueue.SignalFlushed();
                    else
                        TCPQueue.DelayFlush(TCPSendQueueDelay, true);

                    TCPSendBufferOff = TCPSendBufferNumBytes = TCPSendBufferNumPackets = 0;
                    if (TCPDisconnectAfterSendBufferFlush)
                        DisposeSafe();
                    return;

                } catch (SocketException e) {
                    if (e.SocketErrorCode == SocketError.TryAgain || e.SocketErrorCode == SocketError.WouldBlock) {
                        if (++TCPSendBufferRetries < Server.Settings.TCPSendMaxRetries) {
                            Logger.Log(LogLevel.WRN, "tcpudpcon", $"Couldn't flush connection {this} TCP buffer! (Error Code: {e.SocketErrorCode})");
                            TCPTriggerSendBufferFlush = true;
                        } else
                            throw;
                        return;
                    }
                    throw;
                }
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
                    UDPRecvRate.UpdateMetric(dgSize, 0);
                    if (UDPRecvRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkBpTCap && Interlocked.Increment(ref DownlinkCapCounter) >= DownlinkCapCounterMax) {
                        Server.PacketDumper.DumpPacket(this, PacketDumper.TransportType.UDP, "Downlink byte cap hit", buffer, 0, dgSize);
                        Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit UDP downlink byte cap: {UDPRecvRate.ByteRate} BpS {Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkBpTCap} cap BpS");
                        goto closeConnection;
                    }

                    // Let the connection know we received a container
                    ReceivedUDPContainer(buffer[0]);

                    using (MemoryStream mStream = new(buffer, 0, dgSize))
                    using (CelesteNetBinaryReader reader = new(Server.Data, Strings, CoreTypeMap, mStream)) {
                        // Get the container ID
                        byte containerID = reader.ReadByte();

                        // Read packets until we run out data
                        while (mStream.Position < dgSize-1) {
                            DataType packet = Server.Data.Read(reader);

                            // Update metrics
                            UDPRecvRate.UpdateMetric(0, 1);
                            if (UDPRecvRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkPpTCap) {
                                Server.PacketDumper.DumpPacket(this, PacketDumper.TransportType.UDP, "Downlink packet cap hit", buffer, 0, dgSize);
                                Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit UDP downlink packet cap: {UDPRecvRate.PacketRate} PpS {Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkPpTCap} cap PpS");
                                goto closeConnection;
                            }

                            // Handle the packet
                            if (packet.TryGet<MetaOrderedUpdate>(Server.Data, out MetaOrderedUpdate? orderedUpdate))
                                orderedUpdate.UpdateID = containerID;

                            switch (packet) {
                                case DataLowLevelPingReply pingReply: {
                                    _UDPPingMs = (int) (Server.ThreadPool.RuntimeWatch.ElapsedMilliseconds - pingReply.PingTime);
                                } break;
                                default: {
                                    Receive(packet);
                                } break;
                            }
                        }
                    }

                    // Promote optimizations
                    PromoteOptimizations();
                }

                return;
                closeConnection:
                DisposeSafe();
            }
        }

    }
}