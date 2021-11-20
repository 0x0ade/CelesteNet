using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Net;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetTCPUDPConnection : CelesteNetConnection {

        public const int MaxHeartbeatDelay = 8;
        private const int UDPAliveScoreMax = 100;
        private const int UDPDowngradeScoreMin = -1;
        private const int UDPDowngradeScoreMax = 5;
        private const int UDPDeathScoreMin = -1;
        private const int UDPDeathScoreMax = 3;
        
        public readonly RWLock ConLock;

        private bool alive = true;
        public override bool IsConnected => alive && tcpSock.Connected;
        public override string ID { get; }
        public override string UID { get; }

        private Socket tcpSock;
        private EndPoint? udpEP;
        private CelesteNetSendQueue tcpQueue, udpQueue;
        private int maxPacketSize;

        private int udpMaxDatagramSize;
        private byte udpRecvContainerCounter = 0, udpSendContainerCounter = 0;
        private int udpAliveScore = 0, udpDowngradeScore = 0, udpDeathScore = 0;

        private object heartbeatLock = new object();
        private int tcpLastHeartbeatDelay = 0, udpLastHeartbeatDelay = 0;
        private bool tcpSendKeepAlive, udpSendKeepAlive;

        public Socket TCPSocket => tcpSock;

        public bool UseUDP {
            get {
                using (ConLock.R())
                    return udpDeathScore < UDPDeathScoreMax;
            }
        }

        public EndPoint? UDPEndpoint {
            get {
                using (ConLock.R())
                    return (udpMaxDatagramSize > 0) ? udpEP : null;
            }
        }

        public int UDPMaxDatagramSize {
            get {
                using (ConLock.R())
                    return udpMaxDatagramSize;
            }
        }

        public event Action<CelesteNetTCPUDPConnection>? OnUDPDeath;

        public readonly OptMap<string> Strings = new OptMap<string>("StringMap");
        public readonly OptMap<Type> SlimMap = new OptMap<Type>("SlimMap");

        public CelesteNetTCPUDPConnection(DataContext data, Socket tcpSock, string uid, int maxPacketSize, int maxQueueSize, float mergeWindow, Action<CelesteNetSendQueue> tcpQueueFlusher, Action<CelesteNetSendQueue> udpQueueFlusher) : base(data) {
            ConLock = new RWLock();
            ID = $"TCP/UDP uid '{uid}' EP {tcpSock.RemoteEndPoint}";
            UID = uid;

            // Initialize networking stuff
            this.maxPacketSize = maxPacketSize;
            this.tcpSock = tcpSock;
            udpEP = null;
            udpMaxDatagramSize = 0;
            tcpQueue = new CelesteNetSendQueue(this, "TCP Queue", maxQueueSize, mergeWindow, tcpQueueFlusher);
            udpQueue = new CelesteNetSendQueue(this, "UDP Queue", maxQueueSize, mergeWindow, udpQueueFlusher);
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            using (ConLock.W()) {
                alive = false;
                udpEP = null;
                tcpQueue.Dispose();
                udpQueue.Dispose();
                try {
                    tcpSock.Shutdown(SocketShutdown.Both);
                    tcpSock.Close();
                } catch (SocketException) {}
                tcpSock.Dispose();
                ConLock.Dispose();
            }
        }

        protected override CelesteNetSendQueue? GetQueue(DataType data) => (udpEP != null && (data.DataFlags & DataFlags.Unreliable) != 0) ? udpQueue : tcpQueue;

        public bool DoHeartbeatTick() {
            lock (heartbeatLock) {
                if ((tcpLastHeartbeatDelay++) > MaxHeartbeatDelay)
                    return true;
                if (tcpSendKeepAlive)
                    tcpQueue.Enqueue(new DataLowLevelKeepAlive());
                tcpSendKeepAlive = true;

                if (UseUDP && udpEP != null) {
                    if ((udpLastHeartbeatDelay++) > MaxHeartbeatDelay) {
                        udpLastHeartbeatDelay = 0;
                        DowngradeUDP();
                    }

                    if (UseUDP && udpEP != null && udpSendKeepAlive)
                        udpQueue.Enqueue(new DataLowLevelKeepAlive());
                    udpSendKeepAlive = true;
                }
            }
            return false;
        }

        public void InitUDP(EndPoint endpoint, int maxDatagramSize) {
            using (ConLock.W()) {
                udpEP = endpoint;
                udpMaxDatagramSize = maxDatagramSize;
                udpAliveScore = udpDowngradeScore = udpDeathScore = 0;
                Logger.Log(LogLevel.INF, "tcpudpcon", $"Initialized UDP connection of {this} [{udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
            }
        }

        public void IncreaseUDPScore() {
            using (ConLock.W()) {
                if (++udpAliveScore > UDPAliveScoreMax) {
                    udpAliveScore = 0;
                    if (udpDowngradeScore > UDPDowngradeScoreMin)
                        udpDowngradeScore--;
                    else if (udpDeathScore > UDPDeathScoreMin)
                        udpDeathScore--;
                }
            }
        }

        public void DecreaseUDPScore() {
            using (ConLock.W()) {
                udpAliveScore = 0;
                if (++udpDowngradeScore >= UDPDowngradeScoreMax)
                    DowngradeUDP();
            }
        }

        public void HandleUDPInfo(DataLowLevelUDPInfo info) {
            using (ConLock.W()) {
                if (UseUDP && udpEP != null) {
                    if (info.DisableUDP) {
                        udpDeathScore = UDPDeathScoreMax;
                        udpEP = null;
                        udpMaxDatagramSize = 0;
                    } else if (udpMaxDatagramSize > info.MaxDatagramSize)
                        udpMaxDatagramSize = info.MaxDatagramSize;
                }
                Logger.Log(LogLevel.INF, "tcpudpcon", $"Handled remote UDP info from connection {this} [{udpMaxDatagramSize}, {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
                Send(new DataLowLevelUDPInfo() {
                    MaxDatagramSize = udpMaxDatagramSize,
                    DisableUDP = udpDeathScore >= UDPDeathScoreMax
                });
            }
        }

        private void DowngradeUDP() {
            using (ConLock.W()) {
                if (!UseUDP || udpEP == null)
                    return;

                Logger.Log(LogLevel.INF, "tcpudpcon", $"Downgrading UDP connection of {this} [{udpMaxDatagramSize}, {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
                udpAliveScore = 0;
                udpDowngradeScore = 0;
                udpMaxDatagramSize /= 2;
                if (udpMaxDatagramSize < 1+maxPacketSize) {
                    udpEP = null;
                    udpMaxDatagramSize = 0;
                    if (udpDeathScore < UDPDeathScoreMax)
                        udpDeathScore++;
                    udpQueue.Clear();
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"UDP connection of {this} died [{udpMaxDatagramSize}, {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
                    OnUDPDeath?.Invoke(this);
                }
                Send(new DataLowLevelUDPInfo() {
                    MaxDatagramSize = udpMaxDatagramSize,
                    DisableUDP = udpDeathScore >= UDPDeathScoreMax
                });
            }
        }

        public void TCPHeartbeat() {
            lock (heartbeatLock)
                tcpLastHeartbeatDelay = 0;
        }

        public void UDPHeartbeat() {
            lock (heartbeatLock)
                udpLastHeartbeatDelay = 0;
        }

        public void SurpressTCPKeepAlives() {
            lock (heartbeatLock)
                tcpSendKeepAlive = false;
        }

        public void SurpressUDPKeepAlives() {
            lock (heartbeatLock)
                udpSendKeepAlive = false;
        }

        public byte NextUDPContainerID() {
            if (!UseUDP || udpEP == null)
                throw new InvalidOperationException("Connection doesn't have a UDP tunnel");
            byte id = udpSendContainerCounter;
            udpSendContainerCounter = unchecked(udpSendContainerCounter++);
            return id;
        }

    }
}