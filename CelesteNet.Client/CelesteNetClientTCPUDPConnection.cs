using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientTCPUDPConnection : CelesteNetTCPUDPConnection {

        public const int UDPBufferSize = 65536;
        public const int UDPEstablishDelay = 2;

        public readonly CelesteNetClient Client;

        private readonly Stream TCPNetStream, TCPReadStream, TCPWriteStream;
        private readonly Socket UDPSocket;
        private readonly BlockingCollection<DataType?> TCPSendQueue, UDPSendQueue;
        private readonly byte[] UDPHandshakeMessage;

        private readonly CancellationTokenSource TokenSrc;
        private readonly Thread TCPRecvThread, UDPRecvThread, TCPSendThread, UDPSendThread;

        public CelesteNetClientTCPUDPConnection(CelesteNetClient client, uint token, Settings settings, Socket tcpSock) : base(client.Data, token, settings, tcpSock) {
            Client = client;

            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientTCPUDPConnection created");

            // Initialize networking
            TCPNetStream = new NetworkStream(tcpSock);
            TCPReadStream = new BufferedStream(TCPNetStream);
            TCPWriteStream = new BufferedStream(TCPNetStream);
            UDPSocket = new(tcpSock.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            if (tcpSock.RemoteEndPoint != null) {
                UDPSocket.Connect(tcpSock.RemoteEndPoint);
            } else {
                UseUDP = false;
            }

            OnUDPDeath += (_, _) => {
                if (!IsAlive)
                    return;
                Logger.Log(LogLevel.INF, "tcpudpcon", UseUDP ? "UDP connection died" : "Switching to TCP only");
                if (Logger.Level <= LogLevel.DBG)
                    CelesteNetClientModule.Instance?.AnyContext?.Status?.Set(UseUDP ? "UDP connection died" : "Switching to TCP only", 3);
            };

            TCPSendQueue = new();
            UDPSendQueue = new();

            // Create the UDP handshake message
            UDPHandshakeMessage = new byte[] { 0xff }.Concat(BitConverter.GetBytes(ConnectionToken)).ToArray();

            // Start threads
            TokenSrc = new();
            TCPRecvThread = new(TCPRecvThreadFunc) { Name = "CelesteNet.Client TCP Recv Thread" };
            UDPRecvThread = new(UDPRecvThreadFunc) { Name = "CelesteNet.Client UDP Recv Thread" };
            TCPSendThread = new(TCPSendThreadFunc) { Name = "CelesteNet.Client TCP Send Thread" };
            UDPSendThread = new(UDPSendThreadFunc) { Name = "CelesteNet.Client UDP Send Thread" };

            TCPRecvThread.Start();
            UDPRecvThread.Start();
            TCPSendThread.Start();
            UDPSendThread.Start();
        }

        protected override void Dispose(bool disposing) {
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientTCPUDPConnection Dispose called");

            // Wait for threads
            TokenSrc.Cancel();
            TCPSocket.ShutdownSafe(SocketShutdown.Both);
            UDPSocket.Close();

            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientTCPUDPConnection Dispose: Sockets done");

            if (Thread.CurrentThread != TCPRecvThread)
                TCPRecvThread.Join();
            if (Thread.CurrentThread != UDPRecvThread)
                UDPRecvThread.Join();
            if (Thread.CurrentThread != TCPSendThread)
                TCPSendThread.Join();
            if (Thread.CurrentThread != UDPSendThread)
                UDPSendThread.Join();

            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientTCPUDPConnection Dispose: Threads joined");

            base.Dispose(disposing);

            // Dispose stuff
            TokenSrc.Dispose();
            // We must try-catch buffered stream disposes as those will try to flush.
            // If a network stream was torn down out of our control, it will throw!
            try {
                TCPReadStream.Dispose();
            } catch {
            }
            try {
                TCPWriteStream.Dispose();
            } catch {
            }
            TCPNetStream.Dispose();
            UDPSocket.Dispose();
            TCPSendQueue.Dispose();
            UDPSendQueue.Dispose();
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientTCPUDPConnection Dispose: Streams & Queues disposed");
        }

        public override void DisposeSafe() {
            if (!IsAlive || SafeDisposeTriggered)
                return;
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientTCPUDPConnection DisposeSafe set");
            Client.SafeDisposeTriggered = SafeDisposeTriggered = true;
        }

        protected override void FlushTCPQueue() {
            foreach (DataType packet in TCPQueue.BackQueue)
                TCPSendQueue.Add(packet);
            TCPQueue.SignalFlushed();
        }

        protected override void FlushUDPQueue() {
            foreach (DataType packet in UDPQueue.BackQueue)
                UDPSendQueue.Add(packet);
            UDPQueue.SignalFlushed();
        }

        public override string? DoHeartbeatTick() {
            string? disposeReason = base.DoHeartbeatTick();
            if (disposeReason != null || !IsConnected)
                return disposeReason;

            lock (UDPLock) {
                if (UseUDP && UDPEndpoint == null) {
                    // Initialize an unestablished connection and send the handshake message
                    InitUDP(UDPSocket.RemoteEndPoint, -1, 0);
                    UDPSendQueue.Add(null);
                }
            }

            return null;
        }

        private void TCPRecvThreadFunc() {
            try {
                byte[] packetBuffer = new byte[2 + ConnectionSettings.MaxPacketSize];
                void ReadCount(int off, int numBytes) {
                    while (numBytes > 0) {
                        int count = TCPReadStream.Read(packetBuffer, off, numBytes);
                        if (count <= 0)
                            throw new EndOfStreamException();
                        off += count;
                        numBytes -= count;
                    }
                }
                while (!TokenSrc.IsCancellationRequested) {
                    // Read the packet
                    ReadCount(0, 2);
                    ushort packetSize = BitConverter.ToUInt16(packetBuffer, 0);
                    if (packetSize > ConnectionSettings.MaxPacketSize)
                        throw new InvalidDataException("Peer sent packet over maximum size");
                    ReadCount(2, packetSize);

                    // Let the connection know we got a TCP heartbeat
                    TCPHeartbeat();

                    // Read the packet
                    DataType? packet = null;
                    bool otherModException = false;
                    using (MemoryStream packetStream = new(packetBuffer, 2, packetSize))
                    using (CelesteNetBinaryReader packetReader = new(Data, Strings, CoreTypeMap, packetStream)) {
                        try {
                            packet = Data.Read(packetReader);
                        } catch (DataContextException e) {
                            if (e.OtherMod) {
                                Logger.LogDetailedException(e?.InnerException ?? e, "client-data-ex");
                                otherModException = true;
                            } else if (e.InnerException != null)
                                throw e.InnerException;
                            else
                                throw;
                        }
                        if (!otherModException && packetStream.Position != packetSize)
                            throw new InvalidDataException($"Didn't read all data in TCP vdgram ({packetStream.Position} read, {packetSize} total)!");
                    }

                    // Handle the packet
                    if (packet != null) {
                        switch (packet) {
                            case DataLowLevelPingRequest pingReq: {
                                TCPQueue.Enqueue(new DataLowLevelPingReply() {
                                    PingTime = pingReq.PingTime
                                });
                                break;
                            }
                            case DataLowLevelUDPInfo udpInfo: {
                                HandleUDPInfo(udpInfo);
                                break;
                            }
                            case DataLowLevelStringMap strMap: {
                                Strings.RegisterWrite(strMap.String, strMap.ID);
                                break;
                            }
                            case DataLowLevelCoreTypeMap coreTypeMap: {
                                if (coreTypeMap.PacketType != null)
                                    CoreTypeMap.RegisterWrite(coreTypeMap.PacketType, coreTypeMap.ID);
                                break;
                            }
                            default: {
                                Receive(packet);
                                break;
                            }
                        }
                    } else {
                        Logger.Log(LogLevel.WRN, "tcprecv", $"Read null DataType in TCPRecvThreadFunc");
                    }

                    // Promote optimizations
                    PromoteOptimizations();
                }

            } catch (EndOfStreamException) {
                if (!TokenSrc.IsCancellationRequested) {
                    Logger.Log(LogLevel.WRN, "tcprecv", "Remote closed the connection");
                    Client.EndOfStream = true;
                    DisposeSafe();
                }
                return;

            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == TokenSrc.Token)
                    return;

                if (e is ThreadAbortException)
                    return;

                if ((e is IOException || e is SocketException) && TokenSrc.IsCancellationRequested)
                    return;

                Logger.Log(LogLevel.WRN, "tcprecv", $"Error in TCP receiving thread: {e}");
                DisposeSafe();
            }
        }

        private void UDPRecvThreadFunc() {
            try {
                byte[] buffer = new byte[UDPBufferSize];
                while (!TokenSrc.IsCancellationRequested) {
                    try {
                        // Receive a datagram
                        int dgSize = UDPSocket.Receive(buffer);
                        if (dgSize <= 0)
                            continue;

                        // Ignore it if we don't actually have an established connection
                        lock (UDPLock)
                        if (UDPConnectionID < 0)
                            continue;

                        // Let the connection know we received a container
                        ReceivedUDPContainer(buffer[0]);

                        using (MemoryStream mStream = new(buffer, 0, dgSize))
                        using (CelesteNetBinaryReader reader = new(Data, Strings, CoreTypeMap, mStream)) {
                            // Get the container ID
                            byte containerID = reader.ReadByte();

                            // Read packets until we run out data
                            while (mStream.Position < dgSize - 1) {
                                DataType? packet = null;
                                try {
                                    packet = Data.Read(reader);
                                } catch (DataContextException e) {
                                    if (e.OtherMod)
                                        Logger.LogDetailedException(e?.InnerException ?? e, "client-data-ex");
                                    else if (e.InnerException != null)
                                        throw e.InnerException;
                                    else
                                        throw;
                                }
                                if (packet != null && packet.TryGet<MetaOrderedUpdate>(Data, out MetaOrderedUpdate? orderedUpdate))
                                    orderedUpdate.UpdateID = containerID;

                                // Handle packet
                                if (packet != null) {
                                    switch (packet) {
                                        case DataLowLevelPingRequest pingReq: {
                                            UDPQueue.Enqueue(new DataLowLevelPingReply() {
                                                PingTime = pingReq.PingTime
                                            });
                                            break;
                                        }
                                        default: {
                                            Receive(packet);
                                            break;
                                        }
                                    }
                                } else {
                                    Logger.Log(LogLevel.WRN, "udprecv", $"Read null DataType in UDPRecvThreadFunc");
                                }
                            }
                        }

                        // Promote optimizations
                        PromoteOptimizations();

                    } catch (Exception e) {
                        if (e is SocketException se && TokenSrc.IsCancellationRequested)
                            return;

                        Logger.Log(LogLevel.WRN, "udprecv", $"Error in UDP receiving thread: {e}");
                        DecreaseUDPScore(reason: "Error in receive thread");
                    }
                }

            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == TokenSrc.Token)
                    return;

                if (e is ThreadAbortException)
                    return;

                if (e is SocketException && TokenSrc.IsCancellationRequested)
                    return;

                Logger.Log(LogLevel.WRN, "udprecv", $"Error in UDP receiving thread: {e}");
                DisposeSafe();
            }
        }

        private void TCPSendThreadFunc() {
            try {
                using BinaryWriter tcpWriter = new(TCPWriteStream, Encoding.UTF8, true);
                using MemoryStream mStream = new(ConnectionSettings.MaxPacketSize);
                using CelesteNetBinaryWriter bufWriter = new(Data, Strings, CoreTypeMap, mStream);
                foreach (DataType? p in TCPSendQueue.GetConsumingEnumerable(TokenSrc.Token)) {
                    if (!IsConnected)
                        break;

                    // Try to send as many packets as possible
                    for (DataType? packet = p; packet != null; packet = TCPSendQueue.TryTake(out packet) ? packet : null) {
                        // Handle special packets
                        if (packet is DataInternalDisconnect) {
                            tcpWriter.Flush();
                            DisposeSafe();
                            return;
                        }

                        // Send the packet
                        mStream.Position = 0;
                        Data.Write(bufWriter, packet);
                        bufWriter.Flush();
                        int packLen = (int) mStream.Position;

                        tcpWriter.Write((ushort) packLen);
                        tcpWriter.Write(mStream.GetBuffer(), 0, packLen);

                        if (packet is not DataLowLevelKeepAlive)
                            SurpressTCPKeepAlives();
                    }
                    tcpWriter.Flush();
                }

            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == TokenSrc.Token)
                    return;

                if (e is ThreadAbortException)
                    return;

                Logger.Log(LogLevel.WRN, "tcpsend", $"Error in TCP sending thread: {e}");
                DisposeSafe();
            }
        }

        private void UDPSendThreadFunc() {
            try {
                byte[] dgBuffer = new byte[UDPBufferSize];
                using MemoryStream mStream = new(ConnectionSettings.MaxPacketSize);
                using CelesteNetBinaryWriter bufWriter = new(Data, Strings, CoreTypeMap, mStream);
                foreach (DataType? p in UDPSendQueue.GetConsumingEnumerable(TokenSrc.Token)) {
                    try {
                        lock (UDPLock) {
                            if (!UseUDP)
                                return;

                            if (UDPEndpoint != null && UDPConnectionID < 0) {
                                // Try to establish the UDP connection
                                // If it succeeeds, we'll receive a UDPInfo packet with our connection parameters
                                if (p == null)
                                    UDPSocket.Send(UDPHandshakeMessage);
                                continue;
                            } else if (UDPEndpoint == null || p == null)
                                continue;

                            // Try to send as many packets as possible
                            dgBuffer[0] = NextUDPContainerID();
                            int bufOff = 1;
                            for (DataType? packet = p; packet != null; packet = UDPSendQueue.TryTake(out packet, 0) ? packet : null) {
                                mStream.Position = 0;
                                Data.Write(bufWriter, packet);
                                bufWriter.Flush();
                                int packLen = (int) mStream.Position;

                                // Copy packet data to the container buffer
                                if (bufOff + packLen > dgBuffer.Length) {
                                    // Send container & start a new one
                                    UDPSocket.Send(dgBuffer, bufOff, SocketFlags.None);
                                    dgBuffer[0] = NextUDPContainerID();
                                    bufOff = 1;
                                }

                                Buffer.BlockCopy(mStream.GetBuffer(), 0, dgBuffer, bufOff, packLen);
                                bufOff += packLen;

                                if (packet is not DataLowLevelKeepAlive)
                                    SurpressUDPKeepAlives();
                            }

                            // Send the last container
                            if (bufOff > 1)
                                UDPSocket.Send(dgBuffer, bufOff, SocketFlags.None);
                        }

                    } catch (Exception e) {
                        Logger.Log(LogLevel.WRN, "udpsend", $"Error in UDP sending thread: {e}");
                        DecreaseUDPScore(reason: "Error in sending thread");
                    }
                }

            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == TokenSrc.Token)
                    return;

                if (e is ThreadAbortException)
                    return;

                Logger.Log(LogLevel.WRN, "udpsend", $"Error in UDP sending thread: {e}");
                DisposeSafe();
            }
        }

    }
}
