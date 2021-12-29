using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientTCPUDPConnection : CelesteNetTCPUDPConnection {

        public const int TCPBufferSize = 65536;
        public const int UDPBufferSize = 65536;
        public const int UDPEstablishDelay = 2;
        private static readonly byte[] UDPHolePunchMessage = new byte[] { 42 };

        private readonly BufferedSocketStream TCPStream;
        private readonly Socket UDPRecvSocket, UDPSendSocket;
        private readonly BlockingCollection<DataType> TCPSendQueue, UDPSendQueue;

        private readonly CancellationTokenSource TokenSrc;
        private readonly Thread TCPRecvThread, UDPRecvThread, TCPSendThread, UDPSendThread;

        public CelesteNetClientTCPUDPConnection(DataContext data, uint token, Settings settings, Socket tcpSock) : base(data, token, settings, tcpSock, FlushTCPQueue, FlushUDPQueue) {
            // Initialize networking
            tcpSock.Blocking = false;
            TCPStream = new(TCPBufferSize) { Socket = tcpSock };
            IPAddress serverAddr = ((IPEndPoint) tcpSock.RemoteEndPoint).Address;

            UDPRecvSocket = new(SocketType.Dgram, ProtocolType.Udp);
            UDPRecvSocket.EnableEndpointReuse();
            UDPRecvSocket.Connect(serverAddr, settings.UDPSendPort);

            UDPSendSocket = new(SocketType.Dgram, ProtocolType.Udp);
            UDPSendSocket.EnableEndpointReuse();
            UDPSendSocket.Bind(UDPRecvSocket.LocalEndPoint);
            UDPSendSocket.Connect(serverAddr, settings.UDPReceivePort);

            OnUDPDeath += (_, _) => {
                CelesteNetClientModule.Instance?.Context?.Status?.Set(UseUDP ? "UDP connection died" : "Switching to TCP only", 3);
            };

            TCPSendQueue = new();
            UDPSendQueue = new();

            // Start threads
            TokenSrc = new();
            TCPRecvThread = new(TCPRecvThreadFunc) { Name = "CelesteNet.Client TCP Send Thread" };
            UDPRecvThread = new(UDPRecvThreadFunc) { Name = "CelesteNet.Client UDP Send Thread" };
            TCPSendThread = new(TCPSendThreadFunc) { Name = "CelesteNet.Client TCP Recv Thread" };
            UDPSendThread = new(UDPSendThreadFunc) { Name = "CelesteNet.Client UDP Recv Thread" };

            TCPRecvThread.Start();
            UDPRecvThread.Start();
            TCPSendThread.Start();
            UDPSendThread.Start();
        }

        protected override void Dispose(bool disposing) {
            // Wait for threads
            TokenSrc.Cancel();
            TCPSocket.ShutdownSafe(SocketShutdown.Both);
            UDPRecvSocket.Close();
            UDPSendSocket.Close();

            if (Thread.CurrentThread != TCPRecvThread)
                TCPRecvThread.Join();
            if (Thread.CurrentThread != UDPRecvThread)
                UDPRecvThread.Join();
            if (Thread.CurrentThread != TCPSendThread)
                TCPSendThread.Join();
            if (Thread.CurrentThread != UDPSendThread)
                UDPSendThread.Join();

            base.Dispose(disposing);

            // Dispose stuff
            TokenSrc.Dispose();
            TCPStream.Dispose();
            UDPRecvSocket.Dispose();
            UDPSendSocket.Dispose();
            TCPSendQueue.Dispose();
            UDPSendQueue.Dispose();
        }

        public override string DoHeartbeatTick() {
            string disposeReason = base.DoHeartbeatTick();
            if (disposeReason != null)
                return disposeReason;

            lock (UDPLock) {
                if (UseUDP && UDPEndpoint == null) {
                    // Initialize an unestablished connection and send the token
                    InitUDP(UDPSendSocket.RemoteEndPoint, -1, 0);
                    UDPSendQueue.Add(null);
                }

                if (UDPEndpoint != null) {
                    // Punch a hole so that the server's packets can reach us
                    UDPRecvSocket?.Send(UDPHolePunchMessage);
                }
            }

            return null;
        }

        private void TCPRecvThreadFunc() {
            try {
                byte[] packetBuffer = new byte[ConnectionSettings.MaxPacketSize];
                using BinaryReader tcpReader = new(TCPStream, Encoding.UTF8, true);
                while (!TokenSrc.IsCancellationRequested) {
                    // Read the packet
                    ushort packetSize = tcpReader.ReadUInt16();
                    if (packetSize > ConnectionSettings.MaxPacketSize)
                        throw new InvalidDataException("Peer sent packet over maximum size");
                    if (tcpReader.Read(packetBuffer, 0, packetSize) < packetSize)
                        throw new EndOfStreamException();

                    // Let the connection now we got a TCP heartbeat
                    TCPHeartbeat();

                    // Read the packet
                    DataType packet;
                    using (MemoryStream packetStream = new(packetBuffer, 0, packetSize))
                    using (CelesteNetBinaryReader packetReader = new(Data, Strings, SlimMap, packetStream))
                        packet = Data.Read(packetReader);

                    // Handle the packet
                    switch (packet) {
                        case DataLowLevelUDPInfo udpInfo: {
                            HandleUDPInfo(udpInfo);
                            break;
                        }
                        case DataLowLevelStringMap strMap: {
                            Strings.RegisterWrite(strMap.String, strMap.ID);
                            break;
                        }
                        case DataLowLevelSlimMap slimMap: {
                            if (slimMap.PacketType != null)
                                SlimMap.RegisterWrite(slimMap.PacketType, slimMap.ID);
                            break;
                        }
                        default: {
                            Receive(packet);
                            break;
                        }
                    }

                    // Promote optimizations
                    PromoteOptimizations();
                }

            } catch (EndOfStreamException) {
                if (!TokenSrc.IsCancellationRequested) {
                    Logger.Log(LogLevel.WRN, "tcprecv", "Remote closed the connection");
                    DisposeSafe();
                }
                return;

            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == TokenSrc.Token)
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
                        int dgSize = UDPRecvSocket.Receive(buffer);
                        if (dgSize <= 0)
                            continue;

                        // Ignore it if we don't actually have an established connection
                        lock (UDPLock)
                        if (UDPConnectionID < 0)
                            continue;

                        // Let the connection know we received a container
                        ReceivedUDPContainer(buffer[0]);

                        using (MemoryStream mStream = new(buffer, 0, dgSize))
                        using (CelesteNetBinaryReader reader = new(Data, Strings, SlimMap, mStream)) {
                            // Get the container ID
                            byte containerID = reader.ReadByte();

                            // Read packets until we run out data
                            while (mStream.Position < dgSize - 1) {
                                DataType packet = Data.Read(reader);
                                if (packet.TryGet<MetaOrderedUpdate>(Data, out MetaOrderedUpdate orderedUpdate))
                                    orderedUpdate.UpdateID = containerID;
                                Receive(packet);
                            }
                        }

                        // Promote optimizations
                        PromoteOptimizations();

                    } catch (Exception e) {
                        if (e is SocketException se && TokenSrc.IsCancellationRequested)
                            return;

                        Logger.Log(LogLevel.WRN, "udprecv", $"Error in UDP receiving thread: {e}");
                        DecreaseUDPScore();
                    }
                }

            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == TokenSrc.Token)
                    return;

                if (e is SocketException && TokenSrc.IsCancellationRequested)
                    return;

                Logger.Log(LogLevel.WRN, "udprecv", $"Error in UDP receiving thread: {e}");
                DisposeSafe();
            }
        }

        private void TCPSendThreadFunc() {
            try {
                using BinaryWriter tcpWriter = new(TCPStream, Encoding.UTF8, true);
                using MemoryStream mStream = new(ConnectionSettings.MaxPacketSize);
                using CelesteNetBinaryWriter bufWriter = new(Data, Strings, SlimMap, mStream);
                foreach (DataType p in TCPSendQueue.GetConsumingEnumerable(TokenSrc.Token)) {
                    // Try to send as many packets as possible
                    for (DataType packet = p; packet != null; packet = TCPSendQueue.TryTake(out packet) ? packet : null) {
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

                Logger.Log(LogLevel.WRN, "tcpsend", $"Error in TCP sending thread: {e}");
                DisposeSafe();
            }
        }

        private void UDPSendThreadFunc() {
            try {
                byte[] dgBuffer = new byte[UDPBufferSize];
                using MemoryStream mStream = new(ConnectionSettings.MaxPacketSize);
                using CelesteNetBinaryWriter bufWriter = new(Data, Strings, SlimMap, mStream);
                foreach (DataType p in UDPSendQueue.GetConsumingEnumerable(TokenSrc.Token)) {
                    try {
                        lock (UDPLock) {
                            if (!UseUDP)
                                return;

                            if (UDPEndpoint != null && UDPConnectionID < 0) {
                                // Try to establish the UDP connection
                                // If it succeeeds, we'll receive a UDPInfo packet with our connection parameters
                                if (p == null) {
                                    UDPRecvSocket?.Send(UDPHolePunchMessage);
                                    UDPSendSocket.Send(new byte[] { 0xff }.Concat(BitConverter.GetBytes(ConnectionToken)).ToArray());
                                }
                                continue;
                            } else if (UDPEndpoint == null || p == null)
                                continue;

                            // Try to send as many packets as possible
                            dgBuffer[0] = NextUDPContainerID();
                            int bufOff = 1;
                            for (DataType packet = p; packet != null; packet = UDPSendQueue.TryTake(out packet, 0) ? packet : null) {
                                mStream.Position = 0;
                                Data.Write(bufWriter, packet);
                                bufWriter.Flush();
                                int packLen = (int) mStream.Position;

                                // Copy packet data to the container buffer
                                if (bufOff + packLen > dgBuffer.Length) {
                                    // Send container & start a new one
                                    UDPSendSocket.Send(dgBuffer, bufOff, SocketFlags.None);
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
                                UDPSendSocket.Send(dgBuffer, bufOff, SocketFlags.None);
                        }

                    } catch (Exception e) {
                        Logger.Log(LogLevel.WRN, "udpsend", $"Error in UDP sending thread: {e}");
                        DecreaseUDPScore();
                    }
                }

            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == TokenSrc.Token)
                    return;

                Logger.Log(LogLevel.WRN, "udpsend", $"Error in UDP sending thread: {e}");
                DisposeSafe();
            }
        }

        private static void FlushTCPQueue(CelesteNetSendQueue queue) {
            CelesteNetClientTCPUDPConnection con = (CelesteNetClientTCPUDPConnection) queue.Con;
            foreach (DataType packet in queue.BackQueue)
                con.TCPSendQueue.Add(packet);
            queue.SignalFlushed();
        }

        private static void FlushUDPQueue(CelesteNetSendQueue queue) {
            CelesteNetClientTCPUDPConnection con = (CelesteNetClientTCPUDPConnection) queue.Con;
            foreach (DataType packet in queue.BackQueue)
                con.UDPSendQueue.Add(packet);
            queue.SignalFlushed();
        }

    }
}
