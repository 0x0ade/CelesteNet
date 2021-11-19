using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.IO;
using System.Net.Sockets;

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

        public RateMetric TCPRecvRate, TCPSendRate;
        public RateMetric UDPRecvRate, UDPSendRate;

        private byte[] tcpBuffer;
        private int tcpBufferOff;

        public ConPlusTCPUDPConnection(CelesteNetServer server, Socket tcpSock, string uid, TCPReceiverRole tcpReceiver, TCPUDPSenderRole sender) : base(server.Data, tcpSock, uid, server.Settings.MaxQueueSize, server.Settings.MergeWindow, sender.TriggerTCPQueueFlush, sender.TriggerUDPQueueFlush) {
            Server = server;
            TCPReceiver = tcpReceiver;
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
        }
    
        protected override void Dispose(bool disposing) {
            TCPReceiver.Poller.RemoveConnection(this);
            base.Dispose(disposing);
            TCPRecvRate.Dispose();
            TCPSendRate.Dispose();
            UDPRecvRate.Dispose();
            UDPSendRate.Dispose();
        }

        internal void ReceiveTCPData() {
            while (TCPSocket.Available > 0) {
                // Receive data into the buffer
                int numRead = TCPSocket.Receive(tcpBuffer, tcpBuffer.Length - tcpBufferOff, SocketFlags.None);
                tcpBufferOff += numRead;

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
            }

            // Promote optimizations
            PromoteOptimizations();
        }

        public void PromoteOptimizations() {
            foreach ((string str, int id) in Strings.PromoteRead())
                Send(new DataLowLevelStringMap() {
                    String = str,
                    ID = id
                });

            foreach ((Type packetType, int id) in SlimMap.PromoteRead())
                Send(new DataLowLevelSlimMap() {
                    PacketType = packetType,
                    ID = id
                });
        }

        public CelesteNetServer Server { get; }
        public TCPReceiverRole TCPReceiver { get; }
        public TCPUDPSenderRole Sender { get; }

        public bool TCPSendCapped => TCPSendRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkBpTCap || TCPSendRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkPpTCap;
        public float TCPSendCapDelay => Server.Settings.HeuristicSampleWindow * Math.Max(1 - Server.Settings.PlayerTCPUplinkBpTCap / TCPSendRate.ByteRate, 1 - Server.Settings.PlayerTCPUplinkPpTCap / TCPSendRate.PacketRate);
        public bool UDPSendCapped => UDPSendRate.ByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkBpTCap || UDPSendRate.PacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkPpTCap;

    }
}