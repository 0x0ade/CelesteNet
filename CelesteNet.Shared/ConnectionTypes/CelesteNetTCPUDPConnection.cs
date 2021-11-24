using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetTCPUDPConnection : CelesteNetConnection {

        public struct Settings {

            public int UDPReceivePort, UDPSendPort;
            public int MaxPacketSize, MaxQueueSize;
            public float MergeWindow;
            public int MaxHeartbeatDelay;
            public float HeartbeatInterval;
            public int UDPAliveScoreMax;
            public int UDPDowngradeScoreMin, UDPDowngradeScoreMax;
            public int UDPDeathScoreMin, UDPDeathScoreMax;

        }

        public override bool IsConnected => IsAlive && tcpSock.Connected;
        public override string ID { get; }
        public override string UID { get; }
        public readonly OptMap<string> Strings = new OptMap<string>("StringMap");
        public readonly OptMap<Type> SlimMap = new OptMap<Type>("SlimMap");

        public readonly uint ConnectionToken;
        public readonly Settings ConnectionSettings;

        public Socket TCPSocket => tcpSock;

        private Socket tcpSock;
        private EndPoint? udpEP;
        private CelesteNetSendQueue tcpQueue, udpQueue;

        public readonly object UDPLock = new object();
        private int udpConnectionId, udpLastConnectionId = -1;
        private int udpMaxDatagramSize;
        private int udpAliveScore = 0, udpDowngradeScore = 0, udpDeathScore = 0;
        private byte udpNextContainerID = 0;

        public virtual bool UseUDP {
            get {
                lock (UDPLock)
                    return IsConnected && !(ConnectionSettings.UDPReceivePort <= 0 || ConnectionSettings.UDPSendPort <= 0) && udpDeathScore < ConnectionSettings.UDPDeathScoreMax;
            }
            set {
                lock (UDPLock)
                    udpDeathScore = value ? 0 : ConnectionSettings.UDPDeathScoreMax;
            }
        }

        public virtual EndPoint? UDPEndpoint {
            get {
                lock (UDPLock)
                    return UseUDP ? udpEP : null;
            }
        }

        public virtual int UDPConnectionID {
            get {
                lock (UDPLock)
                    return UseUDP ? udpConnectionId : -1;
            }
        }

        public virtual int UDPMaxDatagramSize {
            get {
                lock (UDPLock)
                    return udpMaxDatagramSize;
            }
        }

        public readonly object HeartbeatLock = new object();
        private int tcpLastHeartbeatDelay = 0, udpLastHeartbeatDelay = 0, tcpSendKeepAlive = 0, udpSendKeepAlive = 0;

        public event Action<CelesteNetTCPUDPConnection, EndPoint>? OnUDPDeath;

        public CelesteNetTCPUDPConnection(DataContext data, uint token, Settings settings, Socket tcpSock, Action<CelesteNetSendQueue> tcpQueueFlusher, Action<CelesteNetSendQueue> udpQueueFlusher) : base(data) {
            IPEndPoint serverEp = (IPEndPoint) tcpSock.RemoteEndPoint!;
            ID = $"TCP/UDP {serverEp.Address}:{serverEp.Port}";
            UID = $"con-tcpudp-{BitConverter.ToString(serverEp.Address.GetAddressBytes())}-{serverEp.Port}";

            ConnectionToken = token;
            ConnectionSettings = settings;

            // Initialize networking stuff
            this.tcpSock = tcpSock;
            udpEP = null;
            udpMaxDatagramSize = 0;
            tcpQueue = new CelesteNetSendQueue(this, "TCP Queue", settings.MaxQueueSize, settings.MergeWindow, tcpQueueFlusher);
            udpQueue = new CelesteNetSendQueue(this, "UDP Queue", settings.MaxQueueSize, settings.MergeWindow, udpQueueFlusher);
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            lock (UDPLock) {
                if (udpEP != null) {
                    EndPoint ep = udpEP!;
                    udpEP = null;
                    udpDeathScore = ConnectionSettings.UDPDeathScoreMax;
                    OnUDPDeath?.Invoke(this, ep);
                }
            }

            udpEP = null;
            tcpQueue.Dispose();
            udpQueue.Dispose();
            try {
                tcpSock.ShutdownSafe(SocketShutdown.Both);
                tcpSock.Close();
            } catch (Exception e) {
                Logger.Log(LogLevel.WRN, "tcpudpcon", $"Error while closing TCP socket: {e}");
            }
            tcpSock.Dispose();

        }

        protected override CelesteNetSendQueue? GetQueue(DataType data) => (UseUDP && UDPEndpoint != null && udpConnectionId >= 0 && (data.DataFlags & DataFlags.Unreliable) != 0) ? udpQueue : tcpQueue;

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

                Logger.Log(LogLevel.INF, "tcpudpcon", $"Initialized UDP connection of {this} [{conId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
            }
        }

        public virtual void IncreaseUDPScore() {
            lock (UDPLock) {
                // Must have an initialized connection
                if (UDPEndpoint == null)
                    return;

                // Increment the alive score, then decrement the downgrade score, then the death score
                if (++udpAliveScore > ConnectionSettings.UDPAliveScoreMax) {
                    udpAliveScore = 0;
                    if (udpDowngradeScore > ConnectionSettings.UDPDowngradeScoreMin)
                        udpDowngradeScore--;
                    else if (udpDeathScore > ConnectionSettings.UDPDeathScoreMin)
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
                if (++udpDowngradeScore >= ConnectionSettings.UDPDowngradeScoreMax) {
                    udpDowngradeScore = 0;
                    if ((udpMaxDatagramSize /= 2) >= 1+ConnectionSettings.MaxPacketSize) {
                        Logger.Log(LogLevel.INF, "tcpudpcon", $"Downgrading UDP connection of {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");

                        if (udpConnectionId >= 0)
                            Send(new DataLowLevelUDPInfo() {
                                ConnectionID = udpConnectionId,
                                MaxDatagramSize = udpMaxDatagramSize
                            });
                    } else
                        UDPConnectionDeath(true, "Too many downgrades");
                } else
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Decreased score of UDP connection of {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");

            }
        }

        public virtual void HandleUDPInfo(DataLowLevelUDPInfo info) {
            lock (UDPLock) {
                if (!UseUDP)
                    return;

                // Handle connection ID
                // If the packet contains a negative connection ID, disable UDP
                // If we don't have a connection ID, try to establish the connection
                // Otherwise, the packet must contain our connection ID
                if (info.ConnectionID < 0) {
                    udpAliveScore = udpDowngradeScore = 0;
                    udpDeathScore = ConnectionSettings.UDPDeathScoreMax;
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Remote disabled UDP for connection {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
                    return;
                }

                // We need an initialized connection for the following branches
                if (UDPEndpoint == null)
                    return;

                if (udpConnectionId < 0) {
                    // If it referes to an old connection, just ignore it
                    if (info.ConnectionID <= udpLastConnectionId)
                        return;
                    if (info.MaxDatagramSize < 1+ConnectionSettings.MaxPacketSize)
                        return;
                    udpConnectionId = info.ConnectionID;
                    udpMaxDatagramSize = info.MaxDatagramSize;
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Established UDP connection of {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
                    return;
                } else if (info.ConnectionID != udpConnectionId)
                    return;

                // Check if the remote notified us of a connection death
                if (info.MaxDatagramSize < 1+ConnectionSettings.MaxPacketSize) {
                    UDPConnectionDeath(false, "Remote connection died");
                    return;
                }

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

        private void UDPConnectionDeath(bool increaseScore, string reason) {
            EndPoint ep = udpEP!;

            // Uninitialize the connection
            if (udpConnectionId >= 0)
                udpLastConnectionId = udpConnectionId;
            udpEP = null;
            udpMaxDatagramSize = udpAliveScore = udpDowngradeScore = 0;
            udpQueue.Clear();

            // Increment the death score
            // If it exceeds the maximum, disable UDP
            if (increaseScore && udpDeathScore < ConnectionSettings.UDPDeathScoreMax) {
                if (++udpDeathScore >= ConnectionSettings.UDPDeathScoreMax)
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Disabling UDP for connection {this} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
            }

            // Notify the remote
            if (udpLastConnectionId >= 0)
                Send(new DataLowLevelUDPInfo() {
                    ConnectionID = UseUDP ? udpLastConnectionId : -1,
                    MaxDatagramSize = 0
                });

            Logger.Log(LogLevel.INF, "tcpudpcon", $"UDP connection of {this} died: {reason} [{udpConnectionId} / {udpMaxDatagramSize} / {udpAliveScore} / {udpDowngradeScore} / {udpDeathScore}]");
            OnUDPDeath?.Invoke(this, ep);
        }

        public virtual byte NextUDPContainerID() {
            lock (UDPLock) {
                // Must have an initialized connection
                if (UDPEndpoint == null)
                    throw new InvalidOperationException("No established UDP connection");

                byte id = 0xff;
                while (id == 0xff)
                    id = unchecked(udpNextContainerID++);
                return id;
            }
        }

        public virtual bool DoHeartbeatTick() {
            lock (HeartbeatLock) {
                // Check if we got a TCP heartbeat in the required timeframe
                if (Interlocked.Increment(ref tcpLastHeartbeatDelay) > ConnectionSettings.MaxHeartbeatDelay)
                    return true;

                // Check if we need to send a TCP keep-alive
                if (Interlocked.Exchange(ref tcpSendKeepAlive, 1) > 0)
                    tcpQueue.Enqueue(new DataLowLevelKeepAlive());

                if (UDPEndpoint != null) {
                    // Check if we got a UDP heartbeat in the required timeframe
                    if (Interlocked.Increment(ref udpLastHeartbeatDelay) > ConnectionSettings.MaxHeartbeatDelay) {
                        Volatile.Write(ref udpLastHeartbeatDelay, 0);
                        DecreaseUDPScore();
                    }

                    // Check if we need to send a UDP keep-alive
                    if (UDPEndpoint != null && Interlocked.Exchange(ref udpSendKeepAlive, 1) > 0)
                        udpQueue.Enqueue(new DataLowLevelKeepAlive());
                }
            }
            return false;
        }

        public virtual void TCPHeartbeat() {
            Volatile.Write(ref tcpLastHeartbeatDelay, 0);
        }

        public virtual void UDPHeartbeat() {
            Volatile.Write(ref udpLastHeartbeatDelay, 0);
        }

        public virtual void SurpressTCPKeepAlives() {
            Volatile.Write(ref tcpSendKeepAlive, 0);
        }

        public virtual void SurpressUDPKeepAlives() {
            Volatile.Write(ref udpSendKeepAlive, 0);
        }

    }
}