using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet {
    public abstract class CelesteNetTCPUDPConnection : CelesteNetConnection {

        public struct Settings {

            public int MaxPacketSize, MaxQueueSize;
            public float MergeWindow;
            public int MaxHeartbeatDelay;
            public float HeartbeatInterval;
            public int UDPAliveScoreMax;
            public int UDPDowngradeScoreMin, UDPDowngradeScoreMax;
            public int UDPDeathScoreMin, UDPDeathScoreMax;

        }

        public static string GetConnectionUID(IPEndPoint remoteEp) => $"con-tcpudp-{remoteEp.Address.MapToIPv6().ToString().Replace(':', '-')}";

        protected volatile bool SafeDisposeTriggered = false;
        public override bool IsConnected => IsAlive && !SafeDisposeTriggered && _TCPSock.Connected;
        public override string ID { get; }
        public override string UID { get; }
        public readonly OptMap<string> Strings = new("StringMap");
        public readonly OptMap<Type> CoreTypeMap = new("CoreTypeMap");

        public readonly uint ConnectionToken;
        public readonly Settings ConnectionSettings;

        public Socket TCPSocket => _TCPSock;

        private readonly Socket _TCPSock;
        private EndPoint? _UDPEndpoint;
        protected readonly CelesteNetSendQueue TCPQueue, UDPQueue;

        public const int UDPPacketDropThreshold = 8;
        public readonly object UDPLock = new();
        private volatile int _UDPConnectionID, UDPLastConnectionID = -1;
        private volatile int _UDPMaxDatagramSize;
        private volatile int UDPAliveScore = 0, UDPDowngradeScore = 0, UDPDeathScore = 0;
        private byte UDPRecvLastContainerID = 0, UDPSendNextContainerID = 0;

        public virtual bool UseUDP {
            get => IsConnected && ConnectionSettings.UDPDeathScoreMax >= 0 && UDPDeathScore < ConnectionSettings.UDPDeathScoreMax;
            set => UDPDeathScore = value ? 0 : ConnectionSettings.UDPDeathScoreMax;
        }

        public virtual EndPoint? UDPEndpoint => UseUDP ? _UDPEndpoint : null;

        public virtual int UDPConnectionID => UseUDP ? _UDPConnectionID : -1;

        public virtual int UDPMaxDatagramSize => _UDPMaxDatagramSize;

        public readonly object HeartbeatLock = new();
        private int TCPLastHeartbeatDelay = 0, UDPLastHeartbeatDelay = 0, TCPSendKeepAlive = 0, UDPSendKeepAlive = 0;

        public event Action<CelesteNetTCPUDPConnection, EndPoint>? OnUDPDeath;

        public CelesteNetTCPUDPConnection(DataContext data, uint token, Settings settings, Socket tcpSock) : base(data) {
            IPEndPoint remoteEp = (IPEndPoint) tcpSock.RemoteEndPoint!;
            ID = $"TCP/UDP {remoteEp.Address}:{remoteEp.Port}";
            UID = GetConnectionUID(remoteEp);

            ConnectionToken = token;
            ConnectionSettings = settings;

            // Initialize networking stuff
            _TCPSock = tcpSock;
            _UDPEndpoint = null;
            _UDPMaxDatagramSize = 0;
            TCPQueue = new(this, "TCP Queue", settings.MaxQueueSize, settings.MergeWindow, _ => FlushTCPQueue());
            UDPQueue = new(this, "UDP Queue", settings.MaxQueueSize, settings.MergeWindow, _ => FlushUDPQueue());
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            lock (UDPLock) {
                if (_UDPEndpoint != null) {
                    EndPoint ep = _UDPEndpoint!;
                    _UDPEndpoint = null;
                    UDPDeathScore = ConnectionSettings.UDPDeathScoreMax;
                    OnUDPDeath?.Invoke(this, ep);
                }
            }

            _UDPEndpoint = null;
            TCPQueue.Dispose();
            UDPQueue.Dispose();
            try {
                _TCPSock.ShutdownSafe(SocketShutdown.Both);
                _TCPSock.Close();
            } catch (Exception e) {
                Logger.Log(LogLevel.WRN, "tcpudpcon", $"Error while closing TCP socket: {e}");
            }
            _TCPSock.Dispose();
        }

        public override void DisposeSafe() => throw new NotImplementedException();

        protected override CelesteNetSendQueue? GetQueue(DataType data) => (UseUDP && UDPEndpoint != null && UDPConnectionID >= 0 && (data.DataFlags & DataFlags.Unreliable) != 0) ? UDPQueue : TCPQueue;

        protected abstract void FlushTCPQueue();
        protected abstract void FlushUDPQueue();

        public virtual void PromoteOptimizations() {
            foreach (Tuple<string, int>? promo in Strings.PromoteRead())
                Send(new DataLowLevelStringMap {
                    String = promo.Item1,
                    ID = promo.Item2
                });

            foreach (Tuple<Type, int>? promo in CoreTypeMap.PromoteRead())
                Send(new DataLowLevelCoreTypeMap {
                    PacketType = promo.Item1,
                    ID = promo.Item2
                });
        }

        public virtual void InitUDP(EndPoint endpoint, int conId, int maxDatagramSize) {
            lock (UDPLock) {
                // Can't initialize two connections at once
                if (!UseUDP || UDPEndpoint != null)
                    return;

                // Initialize a new connection
                _UDPEndpoint = endpoint;
                _UDPConnectionID = conId;
                _UDPMaxDatagramSize = maxDatagramSize;
                UDPAliveScore = UDPDowngradeScore = 0;
                UDPRecvLastContainerID = UDPSendNextContainerID = 0;

                // If the connection is already established, send a state update
                if (conId >= 0)
                    Send(new DataLowLevelUDPInfo {
                        ConnectionID = conId,
                        MaxDatagramSize = maxDatagramSize
                    });

                // Immediatly send a keep alive
                UDPQueue.Enqueue(new DataLowLevelKeepAlive());
                Volatile.Write(ref UDPSendKeepAlive, 1);

                Logger.Log(LogLevel.INF, "tcpudpcon", $"Initialized UDP connection of {this} [{conId} / {UDPMaxDatagramSize} / {UDPAliveScore} / {UDPDowngradeScore} / {UDPDeathScore}]");
            }
        }

        public virtual void IncreaseUDPScore() {
            lock (UDPLock) {
                // Must have an initialized connection
                if (UDPEndpoint == null)
                    return;

                // Increment the alive score, then decrement the downgrade score, then the death score
                if (++UDPAliveScore > ConnectionSettings.UDPAliveScoreMax) {
                    UDPAliveScore = 0;
                    if (UDPDowngradeScore > ConnectionSettings.UDPDowngradeScoreMin)
                        UDPDowngradeScore--;
                    else if (UDPDeathScore > ConnectionSettings.UDPDeathScoreMin)
                        UDPDeathScore--;
                }
            }
        }

        public virtual void DecreaseUDPScore(bool downgradeImmediately = false, string? reason = null) {
            lock (UDPLock) {
                // Must have an initialized connection
                if (UDPEndpoint == null)
                    return;

                // Reset the alive score and increment the downgrade score
                UDPAliveScore = 0;
                if (downgradeImmediately || ++UDPDowngradeScore > ConnectionSettings.UDPDowngradeScoreMax) {
                    // Half the maximum datagram size
                    // If we can't do that, the connection died
                    UDPDowngradeScore = 0;
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Downgrading UDP connection of {this}{((reason != null) ? $": {reason}" : string.Empty)} [{_UDPConnectionID} / {UDPMaxDatagramSize} / {UDPAliveScore} / {UDPDowngradeScore} / {UDPDeathScore}]");
                    if ((_UDPMaxDatagramSize /= 2) >= 1 + ConnectionSettings.MaxPacketSize) {
                        if (_UDPConnectionID >= 0)
                            Send(new DataLowLevelUDPInfo {
                                ConnectionID = _UDPConnectionID,
                                MaxDatagramSize = UDPMaxDatagramSize
                            });
                    } else {
                        UDPConnectionDeath(true, "Too many downgrades");
                    }
                } else {
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Decreased score of UDP connection of {this}{((reason != null) ? $": {reason}" : string.Empty)} [{_UDPConnectionID} / {UDPMaxDatagramSize} / {UDPAliveScore} / {UDPDowngradeScore} / {UDPDeathScore}]");
                }

            }
        }

        public virtual void HandleUDPInfo(DataLowLevelUDPInfo info) {
            lock (UDPLock) {
                if (!UseUDP)
                    return;
                UDPHeartbeat();

                // Handle connection ID
                // If the packet contains a negative connection ID, disable UDP
                // If we don't have a connection ID, try to establish the connection
                // Otherwise, the packet must contain our connection ID
                if (info.ConnectionID < 0) {
                    UDPAliveScore = UDPDowngradeScore = 0;
                    UDPDeathScore = ConnectionSettings.UDPDeathScoreMax;
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Remote disabled UDP for connection {this} [{_UDPConnectionID} / {UDPMaxDatagramSize} / {UDPAliveScore} / {UDPDowngradeScore} / {UDPDeathScore}]");
                    return;
                }

                // We need an initialized connection for the following branches
                if (UDPEndpoint == null)
                    return;

                if (_UDPConnectionID < 0) {
                    // If it referes to an old connection, just ignore it
                    if (info.ConnectionID <= UDPLastConnectionID)
                        return;
                    if (info.MaxDatagramSize < 1 + ConnectionSettings.MaxPacketSize)
                        return;
                    _UDPConnectionID = info.ConnectionID;
                    _UDPMaxDatagramSize = info.MaxDatagramSize;
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Established UDP connection of {this} [{_UDPConnectionID} / {UDPMaxDatagramSize} / {UDPAliveScore} / {UDPDowngradeScore} / {UDPDeathScore}]");
                    return;
                }

                if (info.ConnectionID != _UDPConnectionID)
                    return;

                // Check if the remote notified us of a connection death
                if (info.MaxDatagramSize < 1+ConnectionSettings.MaxPacketSize) {
                    UDPConnectionDeath(false, "Remote connection died");
                    return;
                }

                // Decrease the maximum datagram size
                if (info.MaxDatagramSize == UDPMaxDatagramSize)
                    return;
                _UDPMaxDatagramSize = Math.Min(UDPMaxDatagramSize, info.MaxDatagramSize);

                // Notify the remote of the change in our state
                Send(new DataLowLevelUDPInfo {
                    ConnectionID = _UDPConnectionID,
                    MaxDatagramSize = UDPMaxDatagramSize
                });

                Logger.Log(LogLevel.INF, "tcpudpcon", $"Reduced maximum UDP datagram size of connection {this} [{_UDPConnectionID} / {UDPMaxDatagramSize} / {UDPAliveScore} / {UDPDowngradeScore} / {UDPDeathScore}]");
            }
        }

        private void UDPConnectionDeath(bool increaseScore, string reason) {
            EndPoint ep = _UDPEndpoint!;

            // Uninitialize the connection
            if (_UDPConnectionID >= 0)
                UDPLastConnectionID = UDPConnectionID;
            _UDPEndpoint = null;
            _UDPMaxDatagramSize = UDPAliveScore = UDPDowngradeScore = 0;
            UDPQueue.Clear();

            // Increment the death score
            // If it exceeds the maximum, disable UDP
            if (increaseScore && UDPDeathScore < ConnectionSettings.UDPDeathScoreMax) {
                if (++UDPDeathScore > ConnectionSettings.UDPDeathScoreMax)
                    Logger.Log(LogLevel.INF, "tcpudpcon", $"Disabling UDP for connection {this} [{_UDPConnectionID} / {UDPMaxDatagramSize} / {UDPAliveScore} / {UDPDowngradeScore} / {UDPDeathScore}]");
            }

            // Notify the remote
            if (UDPLastConnectionID >= 0)
                Send(new DataLowLevelUDPInfo {
                    ConnectionID = UseUDP ? UDPLastConnectionID : -1,
                    MaxDatagramSize = 0
                });

            Logger.Log(LogLevel.INF, "tcpudpcon", $"UDP connection of {this} died: {reason} [{_UDPConnectionID} / {UDPMaxDatagramSize} / {UDPAliveScore} / {UDPDowngradeScore} / {UDPDeathScore}]");
            OnUDPDeath?.Invoke(this, ep);
        }

        public virtual byte NextUDPContainerID() {
            lock (UDPLock) {
                // Must have an initialized connection
                if (UDPEndpoint == null)
                    throw new InvalidOperationException("No established UDP connection");

                byte id = 0xff;
                while (id == 0xff)
                    id = unchecked(UDPSendNextContainerID++);
                return id;
            }
        }

        protected virtual void ReceivedUDPContainer(byte containerID) {
            UDPHeartbeat();
            lock (UDPLock) {
                // Check if we dropped more packets than the threshold
                int skipCount = (containerID - UDPRecvLastContainerID + 256) % 256;
                if (skipCount <= 128) {
                    if (skipCount > UDPPacketDropThreshold)
                        DecreaseUDPScore(reason: $"Container ID jump over threshold: {UDPRecvLastContainerID} - > {containerID}");
                    UDPRecvLastContainerID = containerID;
                }
            }
        }

        public virtual string? DoHeartbeatTick() {
            if (!IsConnected)
                return null;

            lock (HeartbeatLock) {
                // Check if we got a TCP heartbeat in the required timeframe
                if (Interlocked.Increment(ref TCPLastHeartbeatDelay) > ConnectionSettings.MaxHeartbeatDelay) {
                    DisposeSafe();
                    return $"Connection {this} timed out";
                }

                // Check if we need to send a TCP keep-alive
                if (Interlocked.Exchange(ref TCPSendKeepAlive, 1) > 0)
                    TCPQueue.Enqueue(new DataLowLevelKeepAlive());

                if (UDPEndpoint != null) {
                    // Check if we got a UDP heartbeat in the required timeframe
                    if (Interlocked.Increment(ref UDPLastHeartbeatDelay) > ConnectionSettings.MaxHeartbeatDelay) {
                        Volatile.Write(ref UDPLastHeartbeatDelay, 0);
                        DecreaseUDPScore(true, reason: "No heartbeat in the required timeframe");
                    }

                    // Check if we need to send a UDP keep-alive
                    if (Interlocked.Exchange(ref UDPSendKeepAlive, 1) > 0)
                        UDPQueue.Enqueue(new DataLowLevelKeepAlive());
                }
            }

            return null;
        }

        public virtual void TCPHeartbeat() {
            Volatile.Write(ref TCPLastHeartbeatDelay, 0);
        }

        public virtual void UDPHeartbeat() {
            Volatile.Write(ref UDPLastHeartbeatDelay, 0);
        }

        public virtual void SurpressTCPKeepAlives() {
            Volatile.Write(ref TCPSendKeepAlive, 0);
        }

        public virtual void SurpressUDPKeepAlives() {
            Volatile.Write(ref UDPSendKeepAlive, 0);
        }

    }
}
