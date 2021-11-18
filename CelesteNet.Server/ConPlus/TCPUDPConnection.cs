using System;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet.Server {
    public class ConPlusTCPUDPConnection : CelesteNetTCPUDPConnection {

        private RWLock tcpMetricsLock;
        private long lastTcpByteRateUpdate, lastTcpPacketRateUpdate;
        private float tcpByteRate, tcpPacketRate;

        private RWLock udpMetricsLock;
        private long lastUdpByteRateUpdate, lastUdpPacketRateUpdate;
        private float udpByteRate, udpPacketRate;

        public ConPlusTCPUDPConnection(CelesteNetServer server, Socket tcpSock, string uid, TCPUDPSenderRole sender) : base(server.Data, tcpSock, uid, server.Settings.MergeWindow, sender.TriggerTCPQueueFlush, sender.TriggerUDPQueueFlush) {
            Server = server;
            Sender = sender;
            tcpMetricsLock = new RWLock();
            udpMetricsLock = new RWLock();
        }
    
        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            tcpMetricsLock.Dispose();
            udpMetricsLock.Dispose();
        }

        internal bool UpdateTCPSendMetrics(int byteCount, int packetCount) {
            using (tcpMetricsLock.W()) {
                Sender.Pool.IterateEventHeuristic(ref tcpByteRate, ref lastTcpByteRateUpdate, byteCount, true);
                Sender.Pool.IterateEventHeuristic(ref tcpPacketRate, ref lastTcpPacketRateUpdate, 1, true);
                return tcpByteRate > Server.CurrentUpdateRate * Server.Settings.PlayerTCPUplinkBpUCap || tcpPacketRate > Server.CurrentUpdateRate * Server.Settings.PlayerTCPUplinkPpUCap;
            }
        }

        internal bool UpdateUDPSendMetrics(int byteCount, int packetCount) {
            using (udpMetricsLock.W()) {
                Sender.Pool.IterateEventHeuristic(ref udpByteRate, ref lastUdpByteRateUpdate, byteCount, true);
                Sender.Pool.IterateEventHeuristic(ref udpPacketRate, ref lastUdpPacketRateUpdate, packetCount, true);
                return udpByteRate > Server.CurrentUpdateRate * Server.Settings.PlayerUDPUplinkBpUCap || udpPacketRate > Server.CurrentUpdateRate * Server.Settings.PlayerUDPUplinkPpUCap;
            }
        }

        public CelesteNetServer Server { get; }
        public TCPUDPSenderRole Sender { get; }

        public float TCPByteRate {
            get {
                using (tcpMetricsLock.R())
                    return Server.ThreadPool.IterateEventHeuristic(ref tcpByteRate, ref lastTcpByteRateUpdate, 0);
            }
        }

        public float TCPPacketRate {
            get {
                using (tcpMetricsLock.R())
                    return Server.ThreadPool.IterateEventHeuristic(ref tcpPacketRate, ref lastTcpPacketRateUpdate, 0);
            }
        }

        public bool TCPSendCapped => TCPByteRate > Server.CurrentUpdateRate * Server.Settings.PlayerTCPUplinkBpUCap || TCPPacketRate > Server.CurrentUpdateRate * Server.Settings.PlayerTCPUplinkPpUCap;
        public float TCPSendCapDelay => Server.Settings.HeuristicSampleWindow * Math.Max(1 - Server.Settings.PlayerTCPUplinkBpUCap / TCPByteRate, 1 - Server.Settings.PlayerTCPUplinkPpUCap / TCPPacketRate);

        public float UDPByteRate {
            get {
                using (udpMetricsLock.R())
                    return Server.ThreadPool.IterateEventHeuristic(ref udpByteRate, ref lastUdpByteRateUpdate, 0);
            }
        }

        public float UDPPacketRate {
            get {
                using (udpMetricsLock.R())
                    return Server.ThreadPool.IterateEventHeuristic(ref udpPacketRate, ref lastUdpPacketRateUpdate, 0);
            }
        }

        public bool UDPSendCapped => UDPByteRate > Server.CurrentUpdateRate * Server.Settings.PlayerUDPUplinkBpUCap || UDPPacketRate > Server.CurrentUpdateRate * Server.Settings.PlayerUDPUplinkPpUCap;

    }
}