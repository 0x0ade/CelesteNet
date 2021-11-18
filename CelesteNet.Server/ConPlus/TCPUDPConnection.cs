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

        public ConPlusTCPUDPConnection(CelesteNetServer server, Socket tcpSock, string uid, TCPUDPSenderRole sender) : base(server.Data, tcpSock, uid, server.Settings.MaxQueueSize, server.Settings.MergeWindow, sender.TriggerTCPQueueFlush, sender.TriggerUDPQueueFlush) {
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
                return tcpByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkBpTCap || tcpPacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkPpTCap;
            }
        }

        internal bool UpdateUDPSendMetrics(int byteCount, int packetCount) {
            using (udpMetricsLock.W()) {
                Sender.Pool.IterateEventHeuristic(ref udpByteRate, ref lastUdpByteRateUpdate, byteCount, true);
                Sender.Pool.IterateEventHeuristic(ref udpPacketRate, ref lastUdpPacketRateUpdate, packetCount, true);
                return udpByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkBpTCap || udpPacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkPpTCap;
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

        public bool TCPSendCapped => TCPByteRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkBpTCap || TCPPacketRate > Server.CurrentTickRate * Server.Settings.PlayerTCPUplinkPpTCap;
        public float TCPSendCapDelay => Server.Settings.HeuristicSampleWindow * Math.Max(1 - Server.Settings.PlayerTCPUplinkBpTCap / TCPByteRate, 1 - Server.Settings.PlayerTCPUplinkPpTCap / TCPPacketRate);

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

        public bool UDPSendCapped => UDPByteRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkBpTCap || UDPPacketRate > Server.CurrentTickRate * Server.Settings.PlayerUDPUplinkPpTCap;

    }
}