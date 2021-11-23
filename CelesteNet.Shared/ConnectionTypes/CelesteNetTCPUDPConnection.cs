using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetTCPUDPConnection : CelesteNetConnection {

        public const int MaxHeartbeatDelay = 3;
        private const int UDPAliveScoreMax = 60;
        private const int UDPDowngradeScoreMin = -2;
        private const int UDPDowngradeScoreMax = 3;
        private const int UDPDeathScoreMin = -2;
        private const int UDPDeathScoreMax = 3;
        
        public readonly int ConnectionToken;
        public override bool IsConnected => IsAlive && tcpSock.Connected;
        public override string ID { get; }
        public override string UID { get; }
        public readonly int MaxPacketSize;
        public readonly OptMap<string> Strings = new OptMap<string>("StringMap");
        public readonly OptMap<Type> SlimMap = new OptMap<Type>("SlimMap");

        public Socket TCPSocket => tcpSock;

        private Socket tcpSock;
        private EndPoint? udpEP;
        private CelesteNetSendQueue tcpQueue, udpQueue;

        public readonly object UDPLock = new object();
        private int udpConnectionId, udpLastConnectionId = -1;
        private int udpMaxDatagramSize;
        private int udpAliveScore = 0, udpDowngradeScore = 0, udpDeathScore = 0;
        private byte udpNextContainerID = 0;

        public bool UseUDP {
            get {
                lock (UDPLock)
                    return IsAlive && udpDeathScore < UDPDeathScoreMax;
            }
        }

        public EndPoint? UDPEndpoint {
            get {
                lock (UDPLock)
                    return UseUDP ? udpEP : null;
            }
        }

        public int UDPConnectionID {
            get {
                lock (UDPLock)
                    return UseUDP ? udpConnectionId : -1;
            }
        }

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
            } catch (Exception e) {
                Logger.Log(LogLevel.WRN, "tcpudpcon", $"Error while closing TCP socket: {e}");
            }
            tcpSock.Dispose();

        }

        protected override CelesteNetSendQueue? GetQueue(DataType data) => (UseUDP && UDPEndpoint != null && udpConnectionId > 0 && (data.DataFlags & DataFlags.Unreliable) != 0) ? udpQueue : tcpQueue;

        public virtual void PromoteOptimizations() {
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
        
        public virtual void InitUDP(EndPoint endpoint, int conId, int maxDatagramSize) {
            lock (UDPLock) {
                // Can't initialize two connections at once
                if (!UseUDP || UDPEndpoint != null)
                    return;
                
                // Initialize a new connection
                udpEP = endpoint;
                udpConnectionId = conId;
                udpMaxDatagramSize = maxDatagramSize;
                udpAliveScore = udpDowngradeScore = 0;
                
                // If the connection is already established, send a state update
                if (conId >= 0)
                    Send(new DataLowLevelUDPInfo() {
                        ConnectionID = conId,
                        MaxDatagramSize = maxDatagramSize
                    });

                Logger.Log(LogLevel.INF, "tcpudpcon", $"Initialized UDP connection of {this} [{conId} | {udpMaxDatagramSize} | {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
            }
        }

        public virtual void IncreaseUDPScore() {
            lock (UDPLock) {
                // Must have an initialized connection
                if (UDPEndpoint == null)
                    return;

                // Increment the alive score, then decrement the downgrade score, then the death score
                if (++udpAliveScore > UDPAliveScoreMax) {
                    udpAliveScore = 0;
                    if (udpDowngradeScore > UDPDowngradeScoreMin)
                        udpDowngradeScore--;
                    else if (udpDeathScore > UDPDeathScoreMin)
                        udpDeathScore--;
                }
            }
        }

        public virtual void DecreaseUDPScore() {
            lock (UDPLock) {
                // Must have an initialized connection
                if (UDPEndpoint == null)
                    return;

                // Reset the alive score, half the maximum datagram size, and increment the downgrade score
                // If it reaches it's maximum, the connection died
                udpAliveScore = 0;
                if (++udpDowngradeScore >= UDPDowngradeScoreMax) {
                    udpDowngradeScore = 0;
                    if ((udpMaxDatagramSize /= 2) >= 1+MaxPacketSize) {
                        Logger.Log(LogLevel.INF, "tcpudpcon", $"Downgrading UDP connection of {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");

                        Send(new DataLowLevelUDPInfo() {
                            ConnectionID = udpConnectionId,
                            MaxDatagramSize = udpMaxDatagramSize
                        });
                    } else
                        UDPConnectionDeath("Too many downgrades");
                } else
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Decreased score of UDP connection of {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");

            }
        }

        public virtual void HandleUDPInfo(DataLowLevelUDPInfo info) {
            lock (UDPLock) {
                if (!UseUDP || UDPEndpoint == null)
                    return;

                // Handle connection ID changes
                // Going from a ID to no ID -> connection death
                // Going from no ID to a ID -> connection established 
                if (info.ConnectionID < 0) {
                    if (udpConnectionId >= 0)
                        UDPConnectionDeath("Remote connection died");
                    return;
                } else if (info.ConnectionID >= 0 && udpConnectionId < 0) {
                    // If it referes to an old connection, just ignore it
                    if (info.ConnectionID <= udpLastConnectionId)
                        return;
                    udpConnectionId = info.ConnectionID;
                    udpMaxDatagramSize = info.MaxDatagramSize;
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Established UDP connection of {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
                    return;
                }

                // Check the packet
                if (info.ConnectionID != udpConnectionId)
                    return;
                
                if (info.MaxDatagramSize <= 0)
                    throw new InvalidDataException($"Maximum datagram size can't be smaller than zero for established connections [{info.MaxDatagramSize}]");

                // Decrease the maximum datagram size
                if (info.MaxDatagramSize == udpMaxDatagramSize)
                    return;
                udpMaxDatagramSize = Math.Min(udpMaxDatagramSize, info.MaxDatagramSize);

                // Notify the remote of the change in our state
                Send(new DataLowLevelUDPInfo() {
                    ConnectionID = udpConnectionId,
                    MaxDatagramSize = udpMaxDatagramSize
                });
                
                Logger.Log(LogLevel.INF, "tcpudpcon", $"Reduced maximum UDP datagram size of connection {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
            }
        }

        private void UDPConnectionDeath(string reason) {
            EndPoint ep = udpEP!;

            if (udpConnectionId >= 0) {
                udpLastConnectionId = udpConnectionId;

                // Notify the remote of our connection death if the connection was established
                Send(new DataLowLevelUDPInfo() {
                    ConnectionID = -1,
                    MaxDatagramSize = 0
                });
            }

            // Uninitialize the connection
            udpEP = null;
            udpMaxDatagramSize = udpAliveScore = udpDowngradeScore = 0;
            udpQueue.Clear();

            // Increment the death score
            // If it exceeds the maximum, disable UDP
            if (udpDeathScore < UDPDeathScoreMax)
                udpDeathScore++;

            Logger.Log(LogLevel.INF, "tcpudpcon", $"UDP connection of {this} died: {reason} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
            OnUDPDeath?.Invoke(this, ep);
        }

        public virtual byte NextUDPContainerID() {
            lock (UDPLock) {
                // Must have an initialized connection
                if (UDPEndpoint == null)
                    throw new InvalidOperationException("No established UDP connection");
                return unchecked(udpNextContainerID++);
            }
        }

        public virtual bool DoHeartbeatTick() {
            lock (HeartbeatLock) {
                // Check if we got a TCP heartbeat in the required timeframe
                if ((tcpLastHeartbeatDelay++) > MaxHeartbeatDelay)
                    return true;

                // Check if we need to send a TCP keep-alive
                if (tcpSendKeepAlive)
                    tcpQueue.Enqueue(new DataLowLevelKeepAlive());
                tcpSendKeepAlive = true;

                if (UDPEndpoint != null) {
                    // Check if we got a UDP heartbeat in the required timeframe
                    if ((udpLastHeartbeatDelay++) > MaxHeartbeatDelay) {
                        udpLastHeartbeatDelay = 0;
                        DecreaseUDPScore();
                    }

                    // Check if we need to send a UDP keep-alive
                    if (UDPEndpoint != null && udpSendKeepAlive)
                        udpQueue.Enqueue(new DataLowLevelKeepAlive());
                    udpSendKeepAlive = true;
                }
            }
            return false;
        }

        public virtual void TCPHeartbeat() {
            lock (HeartbeatLock)
                tcpLastHeartbeatDelay = 0;
        }

        public virtual void UDPHeartbeat() {
            lock (HeartbeatLock)
                udpLastHeartbeatDelay = 0;
        }

        public virtual void SurpressTCPKeepAlives() {
            lock (HeartbeatLock)
                tcpSendKeepAlive = false;
        }

        public virtual void SurpressUDPKeepAlives() {
            lock (HeartbeatLock)
                udpSendKeepAlive = false;
        }

    }
}