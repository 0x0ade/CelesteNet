using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public class ConPlusTCPUDPConnection : CelesteNetTCPUDPConnection {

        public class RateMetric : IDisposable {

            public readonly ConPlusTCPUDPConnection Con;
            private RWLock rateLock;
            private long lastByteRateUpdate, lastPacketRateUpdate;
            private float byteRate, packetRate;

            internal RateMetric(ConPlusTCPUDPConnection con) {
                Con = con;
                rateLock = new RWLock();
                lastByteRateUpdate = lastPacketRateUpdate = 0;
                byteRate = packetRate = 0;
            }

            public void Dispose() {
                rateLock.Dispose();
            }

            public void UpdateRate(int byteCount, int packetCount) {
                using (rateLock.W()) {
                    Con.Server.ThreadPool.IterateEventHeuristic(ref byteRate, ref lastByteRateUpdate, byteCount, true);
                    Con.Server.ThreadPool.IterateEventHeuristic(ref packetRate, ref lastPacketRateUpdate, packetCount, true);
                }
            }

            public float ByteRate {
                get {
                    using (rateLock.R())
                        return Con.Server.ThreadPool.IterateEventHeuristic(ref byteRate, ref lastByteRateUpdate, 0);
                }
            }

            public float PacketRate {
                get {
                    using (rateLock.R())
                        return Con.Server.ThreadPool.IterateEventHeuristic(ref packetRate, ref lastPacketRateUpdate, 0);
                }
            }

        }

        public readonly CelesteNetServer Server;
        public readonly TCPReceiverRole TCPReceiver;
        public readonly UDPReceiverRole UDPReceiver;
        public readonly TCPUDPSenderRole Sender;

        public readonly RateMetric TCPRecvRate, TCPSendRate;
        public readonly RateMetric UDPRecvRate, UDPSendRate;

        private byte[] tcpBuffer;
        private int tcpBufferOff;

        public ConPlusTCPUDPConnection(CelesteNetServer server, int token, string uid, Socket tcpSock, TCPReceiverRole tcpReceiver, UDPReceiverRole udpReceiver, TCPUDPSenderRole sender) : base(server.Data, token, uid, server.Settings.MaxPacketSize, server.Settings.MaxQueueSize, server.Settings.MergeWindow, tcpSock, sender.TriggerTCPQueueFlush, sender.TriggerUDPQueueFlush) {
            Server = server;
            TCPReceiver = tcpReceiver;
            UDPReceiver = udpReceiver;
            Sender = sender;
            TCPRecvRate = new RateMetric(this);
            TCPSendRate = new RateMetric(this);
            UDPRecvRate = new RateMetric(this);
            UDPSendRate = new RateMetric(this);

            // Initialize TCP receiving
            tcpSock.Blocking = false;
            tcpBuffer = new byte[Math.Max(server.Settings.TCPBufferSize, 2+server.Settings.MaxPacketSize)];
            tcpBufferOff = 0;
            tcpReceiver.Poller.AddConnection(this);

            // Initialize UDP receiving
            udpReceiver.AddConnection(this);
        }
    
        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            TCPReceiver.Poller.RemoveConnection(this);
            UDPReceiver.RemoveConnection(this);
            TCPRecvRate.Dispose();
            TCPSendRate.Dispose();
            UDPRecvRate.Dispose();
            UDPSendRate.Dispose();
        }

        public void ReceiveTCPData() {
            if (!IsConnected)
                return;
            do {
                // Receive data into the buffer
                int numRead = TCPSocket.Receive(tcpBuffer, tcpBuffer.Length - tcpBufferOff, SocketFlags.None);
                if (numRead == 0) {
                    // The remote closed the connection
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Remote of connection {this} closed the connection");
                    Dispose();
                    return;
                }
                tcpBufferOff += numRead;

                // Make the connection know we got a heartbeat
                TCPHeartbeat();

                // Update metrics and check if we hit the cap
                TCPRecvRate.UpdateRate(numRead, 0);
                if (TCPRecvRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkBpTCap) {
                    Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit TCP downlink byte cap: {TCPRecvRate.ByteRate} BpS {Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkBpTCap} cap BpS");
                    Dispose();
                    return;
                }

                // Check if we have read the first two length bytes
                if (tcpBufferOff >= 2) {
                    // Get the packet length
                    int packetLen = BitConverter.ToUInt16(tcpBuffer, 0);
                    if (packetLen > Server.Settings.MaxPacketSize) {
                        Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} sent packet over the size limit: {packetLen} > {Server.Settings.MaxPacketSize}");
                        Dispose();
                        return;
                    }

                    // Did we receive the entire packet already?
                    if (2+packetLen <= tcpBufferOff) {
                        // Update metrics and check if we hit the cap
                        TCPRecvRate.UpdateRate(0, 1);
                        if (TCPRecvRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkPpTCap) {
                            Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit TCP downlink packet cap: {TCPRecvRate.PacketRate} PpS {Server.CurrentTickRate * Server.Settings.PlayerTCPDownlinkPpTCap} cap PpS");
                            Dispose();
                            return;
                        }

                        // Read the packet
                        DataType packet;
                        using (MemoryStream mStream = new MemoryStream(tcpBuffer, 2, packetLen))
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
                        Buffer.BlockCopy(tcpBuffer, 2+packetLen, tcpBuffer, 0, tcpBufferOff - (2+packetLen));
                    }
                }
            } while (TCPSocket.Available > 0);

            // Promote optimizations
            PromoteOptimizations();
        }

        public void HandleUDPDatagram(byte[] buffer, int dgSize) {
            if (!IsConnected || dgSize <= 0)
                return;

            // Update metrics
            UDPRecvRate.UpdateRate(dgSize, 0);
            if (UDPRecvRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkBpTCap) {
                Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit UDP downlink byte cap: {UDPRecvRate.ByteRate} BpS {Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkBpTCap} cap BpS");
                Dispose();
                return;
            }
            
            // Get the container ID
            byte containerID = buffer[0];

            // Read packets until we run out data
            using (MemoryStream mStream = new MemoryStream(buffer, 1, dgSize-1))
            using (CelesteNetBinaryReader reader = new CelesteNetBinaryReader(Server.Data, Strings, SlimMap, mStream))
            while (mStream.Position < dgSize-1) {
                DataType packet = Server.Data.Read(reader);
                
                // Update metrics
                UDPRecvRate.UpdateRate(0, 1);
                if (UDPRecvRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkPpTCap) {
                    Logger.Log(LogLevel.WRN, "tcpudpcon", $"Connection {this} hit UDP downlink packet cap: {UDPRecvRate.PacketRate} PpS {Server.CurrentTickRate * Server.Settings.PlayerUDPDownlinkPpTCap} cap PpS");
                    Dispose();
                    return;
                }

                if (packet.TryGet<MetaOrderedUpdate>(Server.Data, out MetaOrderedUpdate? orderedUpdate))
                    orderedUpdate.UpdateID = containerID;
                Receive(packet);
            }

            // Promote optimizations
            PromoteOptimizations();
        }

        public bool TCPSendCapped => TCPSendRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkBpTCap || TCPSendRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkPpTCap;
        public float TCPSendCapDelay => Math.Max(Server.Settings.HeuristicSampleWindow * Math.Max(1 - Server.Settings.PlayerTCPUplinkBpTCap / TCPSendRate.ByteRate, 1 - Server.Settings.PlayerTCPUplinkPpTCap / TCPSendRate.PacketRate), 0);
        public bool UDPSendCapped => UDPSendRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkBpTCap || UDPSendRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkPpTCap;

    }
}