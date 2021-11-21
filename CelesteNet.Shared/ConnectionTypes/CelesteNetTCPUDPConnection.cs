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
        
        public readonly int ConnectionToken;
        public override bool IsConnected => IsAlive && tcpSock.Connected;
        public override string ID { get; }
        public override string UID { get; }
        public readonly int MaxPacketSize;
        public readonly OptMap<string> Strings = new OptMap<string>("StringMap");
        public readonly OptMap<Type> SlimMap = new OptMap<Type>("SlimMap");

        public Socket TCPSocket => tcpSock;

        public bool UseUDP {
            get {
                lock (UDPLock)
                    return IsAlive && udpDeathScore < UDPDeathScoreMax;
            }
        }

        public EndPoint? UDPEndpoint {
            get {
                lock (UDPLock)
                    return (UseUDP && udpMaxDatagramSize > 0) ? udpEP : null;
            }
        }

        private Socket tcpSock;
        private EndPoint? udpEP;
        private CelesteNetSendQueue tcpQueue, udpQueue;

        public readonly object UDPLock = new object();
        private int udpMaxDatagramSize;
        private byte udpSendContainerCounter = 0;
        private int udpAliveScore = 0, udpDowngradeScore = 0, udpDeathScore = 0;

        public int UDPMaxDatagramSize {
            get {
                lock (UDPLock)
                    return udpMaxDatagramSize;
            }
        }

        public readonly object HeartbeatLock = new object();
        private int tcpLastHeartbeatDelay = 0, udpLastHeartbeatDelay = 0;
        private bool tcpSendKeepAlive, udpSendKeepAlive;

        public event Action<CelesteNetTCPUDPConnection, EndPoint>? OnUDPDeath;

        public CelesteNetTCPUDPConnection(DataContext data, int token,  string uid, int maxPacketSize, int maxQueueSize, float mergeWindow, Socket tcpSock, Action<CelesteNetSendQueue> tcpQueueFlusher, Action<CelesteNetSendQueue> udpQueueFlusher) : base(data) {
            ConnectionToken = token;
            ID = $"TCP/UDP uid '{uid}' EP {tcpSock.RemoteEndPoint}";
            UID = uid;

            // Initialize networking stuff
            MaxPacketSize = maxPacketSize;
            this.tcpSock = tcpSock;
            udpEP = null;
            udpMaxDatagramSize = 0;
            tcpQueue = new CelesteNetSendQueue(this, "TCP Queue", maxQueueSize, mergeWindow, tcpQueueFlusher);
            udpQueue = new CelesteNetSendQueue(this, "UDP Queue", maxQueueSize, mergeWindow, udpQueueFlusher);
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            lock (UDPLock) {
                if (udpEP != null) {
                    EndPoint ep = udpEP!;
                    udpEP = null;
                    udpDeathScore = UDPDeathScoreMax;
                    OnUDPDeath?.Invoke(this, ep);
                }
            }
            
            udpEP = null;
            tcpQueue.Dispose();
            udpQueue.Dispose();
            try {
                tcpSock.Shutdown(SocketShutdown.Both);
                tcpSock.Close();
            } catch (SocketException) {}
            tcpSock.Dispose();

        }

        protected override CelesteNetSendQueue? GetQueue(DataType data) => (udpEP != null && (data.DataFlags & DataFlags.Unreliable) != 0) ? udpQueue : tcpQueue;

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
        
        public void InitUDP(EndPoint endpoint, int maxDatagramSize) {
            lock (UDPLock) {
                udpEP = endpoint;
                udpMaxDatagramSize = maxDatagramSize;
                udpAliveScore = udpDowngradeScore = udpDeathScore = 0;
                Logger.Log(LogLevel.INF, "tcpudpcon", $"Initialized UDP connection of {this} [{udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
            }
        }

        public void IncreaseUDPScore() {
            lock (UDPLock) {
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
            lock (UDPLock) {
                udpAliveScore = 0;
                if (++udpDowngradeScore >= UDPDowngradeScoreMax)
                    DowngradeUDP();
            }
        }

        public void HandleUDPInfo(DataLowLevelUDPInfo info) {
            lock (UDPLock) {
                if (UDPEndpoint != null) {
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
            lock (UDPLock) {
                if (UDPEndpoint == null)
                    return;

                Logger.Log(LogLevel.INF, "tcpudpcon", $"Downgrading UDP connection of {this} [{udpMaxDatagramSize}, {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
                udpAliveScore = 0;
                udpDowngradeScore = 0;
                udpMaxDatagramSize /= 2;
                if (udpMaxDatagramSize < 1+MaxPacketSize) {
                    EndPoint ep = udpEP!;
                    udpEP = null;
                    udpMaxDatagramSize = 0;
                    if (udpDeathScore < UDPDeathScoreMax)
                        udpDeathScore++;
                    udpQueue.Clear();
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"UDP connection of {this} died [{udpMaxDatagramSize}, {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
                    OnUDPDeath?.Invoke(this, ep);
                }
                Send(new DataLowLevelUDPInfo() {
                    MaxDatagramSize = udpMaxDatagramSize,
                    DisableUDP = udpDeathScore >= UDPDeathScoreMax
                });
            }
        }

        public bool DoHeartbeatTick() {
            lock (HeartbeatLock) {
                if ((tcpLastHeartbeatDelay++) > MaxHeartbeatDelay)
                    return true;
                if (tcpSendKeepAlive)
                    tcpQueue.Enqueue(new DataLowLevelKeepAlive());
                tcpSendKeepAlive = true;

                if (UDPEndpoint != null) {
                    if ((udpLastHeartbeatDelay++) > MaxHeartbeatDelay) {
                        udpLastHeartbeatDelay = 0;
                        DowngradeUDP();
                    }

                    if (UDPEndpoint != null && udpSendKeepAlive)
                        udpQueue.Enqueue(new DataLowLevelKeepAlive());
                    udpSendKeepAlive = true;
                }
            }
            return false;
        }

        public void TCPHeartbeat() {
            lock (HeartbeatLock)
                tcpLastHeartbeatDelay = 0;
        }

        public void UDPHeartbeat() {
            lock (HeartbeatLock)
                udpLastHeartbeatDelay = 0;
        }

        public void SurpressTCPKeepAlives() {
            lock (HeartbeatLock)
                tcpSendKeepAlive = false;
        }

        public void SurpressUDPKeepAlives() {
            lock (HeartbeatLock)
                udpSendKeepAlive = false;
        }

        public byte NextUDPContainerID() {
            if (UDPEndpoint == null)
                throw new InvalidOperationException("Connection doesn't have a UDP tunnel");
            byte id = udpSendContainerCounter;
            udpSendContainerCounter = unchecked(udpSendContainerCounter++);
            return id;
        }

    }
}