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

        public const int TCPBufferSize = 16384;
        public const int UDPBufferSize = 16384;
        public const int UDPEstablishDelay = 2;
        private static readonly byte[] UDPHolePunchMessage = new byte[] { 42 };

        private BufferedSocketStream tcpStream;
        private Socket udpRecvSocket, udpSendSocket;
        private BlockingCollection<DataType> tcpSendQueue, udpSendQueue;

        private CancellationTokenSource tokenSrc;
        private Thread tcpRecvThread, udpRecvThread, tcpSendThread, udpSendThread;

        public CelesteNetClientTCPUDPConnection(DataContext data, uint token, Settings settings, Socket tcpSock) : base(data, token, settings, tcpSock, FlushTCPQueue, FlushUDPQueue) {
            // Initialize networking
            tcpSock.Blocking = false;
            tcpStream = new BufferedSocketStream(TCPBufferSize) { Socket = tcpSock };
            IPAddress serverAddr = ((IPEndPoint) tcpSock.RemoteEndPoint).Address;

            udpRecvSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            udpRecvSocket.EnableEndpointReuse();
            udpRecvSocket.Connect(serverAddr, settings.UDPSendPort);

            udpSendSocket = new Socket(SocketType.Dgram, ProtocolType.Udp);
            udpSendSocket.EnableEndpointReuse();
            udpSendSocket.Bind(udpRecvSocket.LocalEndPoint);
            udpSendSocket.Connect(serverAddr, settings.UDPReceivePort);

            OnUDPDeath += (_, _) => {
                if (CelesteNetClientModule.Instance?.Context?.Status != null)
                    CelesteNetClientModule.Instance.Context.Status.Set(UseUDP ? "UDP connection died" : "Switching to TCP only", 3);
            };

            tcpSendQueue = new BlockingCollection<DataType>();
            udpSendQueue = new BlockingCollection<DataType>();

            // Start threads
            tokenSrc = new CancellationTokenSource();
            tcpRecvThread = new Thread(TCPRecvThreadFunc) { Name = "TCP Send Thread" };
            udpRecvThread = new Thread(UDPRecvThreadFunc) { Name = "UDP Send Thread" };
            tcpSendThread = new Thread(TCPSendThreadFunc) { Name = "TCP Recv Thread" };
            udpSendThread = new Thread(UDPSendThreadFunc) { Name = "UDP Recv Thread" };

            tcpRecvThread.Start();
            udpRecvThread.Start();
            tcpSendThread.Start();
            udpSendThread.Start();
        }

        protected override void Dispose(bool disposing) {
            // Wait for threads
            tokenSrc.Cancel();
            TCPSocket.ShutdownSafe(SocketShutdown.Both);
            udpRecvSocket.Close();
            udpSendSocket.Close();

            if (Thread.CurrentThread != tcpRecvThread)
                tcpRecvThread.Join();
            if (Thread.CurrentThread != udpRecvThread)
                udpRecvThread.Join();
            if (Thread.CurrentThread != tcpSendThread)
                tcpSendThread.Join();
            if (Thread.CurrentThread != udpSendThread)
                udpSendThread.Join();

            base.Dispose(disposing);

            // Dispose stuff
            tokenSrc.Dispose();
            tcpStream.Dispose();
            udpRecvSocket.Dispose();
            udpSendSocket.Dispose();
            tcpSendQueue.Dispose();
            udpSendQueue.Dispose();
        }

        private void DisposeSafe() {
            try {
                if(!tokenSrc.IsCancellationRequested)
                    Dispose();
            } catch (Exception e2) {
                Logger.Log(LogLevel.WRN, "tpcudpcon", $"Error disposing connection: {e2}");
            }
        }

        public override bool DoHeartbeatTick() {
            if (base.DoHeartbeatTick())
                return true;

            lock (UDPLock) {
                if (UseUDP && UDPEndpoint == null) {
                    // Initialize an unestablished connection and send the token
                    InitUDP(udpSendSocket.RemoteEndPoint, -1, 0);
                    udpSendQueue.Add(null);
                }

                if (UDPEndpoint != null) {
                    // Punch a hole so that the server's packets can reach us
                    udpRecvSocket?.Send(UDPHolePunchMessage);
                }
            }

            return false;
        }

        private void TCPRecvThreadFunc() {
            try {
                byte[] packetBuffer = new byte[ConnectionSettings.MaxPacketSize];
                using (BinaryReader tcpReader = new BinaryReader(tcpStream, Encoding.UTF8, true)) {
                    while (!tokenSrc.IsCancellationRequested) {
                        // Read the packet
                        UInt16 packetSize = tcpReader.ReadUInt16();
                        if (packetSize > ConnectionSettings.MaxPacketSize)
                            throw new InvalidDataException("Peer sent packet over maximum size");
                        if (tcpReader.Read(packetBuffer, 0, packetSize) < packetSize)
                            throw new EndOfStreamException();

                        // Let the connection now we got a TCP heartbeat
                        TCPHeartbeat();

                        // Read the packet
                        DataType packet;
                        using (MemoryStream packetStream = new MemoryStream(packetBuffer, 0, packetSize))
                        using (CelesteNetBinaryReader packetReader = new CelesteNetBinaryReader(Data, Strings, SlimMap, packetStream))
                            packet = Data.Read(packetReader);

                        // Handle the packet
                        switch (packet) {
                            case DataLowLevelUDPInfo udpInfo: {
                                HandleUDPInfo(udpInfo);
                            } break;
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

                        // Promote optimizations
                        PromoteOptimizations();
                    }
                }
            } catch (EndOfStreamException) {
                if (!tokenSrc.IsCancellationRequested) {
                    Logger.Log(LogLevel.WRN, "tcprecv", "Remote closed the connection");
                    DisposeSafe();
                }
                return;
            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == tokenSrc.Token)
                    return;

                if ((e is IOException || e is SocketException) && tokenSrc.IsCancellationRequested)
                    return;

                Logger.Log(LogLevel.WRN, "tcprecv", $"Error in TCP receiving thread: {e}");
                DisposeSafe();
            }
        }

        private void UDPRecvThreadFunc() {
            try {
                byte[] buffer = new byte[UDPBufferSize];
                while (!tokenSrc.IsCancellationRequested) {
                    try {
                        // Receive a datagram
                        int dgSize = udpRecvSocket.Receive(buffer);
                        if (dgSize == 0)
                            continue;

                        // Ignore it if we don't actually have an established connection
                        lock (UDPLock)
                        if (UDPConnectionID < 0)
                            continue;

                        // Let the connection know we received a container
                        ReceivedUDPContainer(buffer[0]);

                        using (MemoryStream mStream = new MemoryStream(buffer, 0, dgSize))
                        using (CelesteNetBinaryReader reader = new CelesteNetBinaryReader(Data, Strings, SlimMap, mStream)) {
                            // Get the container ID
                            byte containerID = reader.ReadByte();

                            // Read packets until we run out data
                            while (mStream.Position < dgSize-1) {
                                DataType packet = Data.Read(reader);
                                if (packet.TryGet<MetaOrderedUpdate>(Data, out MetaOrderedUpdate orderedUpdate))
                                    orderedUpdate.UpdateID = containerID;
                                Receive(packet);
                            }
                        }

                        // Promote optimizations
                        PromoteOptimizations();
                    } catch (Exception e) {
                        if (e is SocketException se && tokenSrc.IsCancellationRequested)
                            return;

                        Logger.Log(LogLevel.WRN, "udprecv", $"Error in UDP receiving thread: {e}");
                        DecreaseUDPScore();
                    }
                }
            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == tokenSrc.Token)
                    return;
                
                if (e is SocketException && tokenSrc.IsCancellationRequested)
                    return;

                Logger.Log(LogLevel.WRN, "udprecv", $"Error in UDP receiving thread: {e}");
                DisposeSafe();
            }
        }

        private void TCPSendThreadFunc() {
            try {
                using (BinaryWriter tcpWriter = new BinaryWriter(tcpStream, Encoding.UTF8, true))
                using (MemoryStream mStream = new MemoryStream(ConnectionSettings.MaxPacketSize))
                using (CelesteNetBinaryWriter bufWriter = new CelesteNetBinaryWriter(Data, Strings, SlimMap, mStream))
                foreach (DataType p in tcpSendQueue.GetConsumingEnumerable(tokenSrc.Token)) {
                    // Try to send as many packets as possible
                    for (DataType packet = p; packet != null; tcpSendQueue.TryTake(out packet)) {
                        mStream.Position = 0;
                        Data.Write(bufWriter, packet);
                        bufWriter.Flush();
                        int packLen = (int) mStream.Position;

                        tcpWriter.Write((UInt16) packLen);
                        tcpWriter.Write(mStream.GetBuffer(), 0, packLen);

                        if (!(packet is DataLowLevelKeepAlive))
                            SurpressTCPKeepAlives();
                    }
                    tcpWriter.Flush();
                }
            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == tokenSrc.Token)
                    return;

                Logger.Log(LogLevel.WRN, "tcpsend", $"Error in TCP sending thread: {e}");
                DisposeSafe();
            }
        }

        private void UDPSendThreadFunc() {
            try {
                byte[] dgBuffer = null;
                using (MemoryStream mStream = new MemoryStream(ConnectionSettings.MaxPacketSize))
                using (CelesteNetBinaryWriter bufWriter = new CelesteNetBinaryWriter(Data, Strings, SlimMap, mStream))
                foreach (DataType p in udpSendQueue.GetConsumingEnumerable(tokenSrc.Token)) {
                    try {
                        lock (UDPLock) {
                            if (!UseUDP)
                                return;

                            if (UDPEndpoint != null && UDPConnectionID < 0) {
                                // Try to establish the UDP connection
                                // If it succeeeds, we'll receive a UDPInfo packet with our connection parameters
                                if (p == null) {
                                    udpRecvSocket?.Send(UDPHolePunchMessage);
                                    udpSendSocket.Send(new byte[] { 0xff }.Concat(BitConverter.GetBytes(ConnectionToken)).ToArray());
                                }
                                continue;
                            } else if (UDPEndpoint == null || p == null)
                                continue;

                            // Adjust the datagram buffer size
                            if (dgBuffer == null || dgBuffer.Length != UDPMaxDatagramSize)
                                Array.Resize(ref dgBuffer, UDPMaxDatagramSize);

                            // Try to send as many packets as possible
                            dgBuffer[0] = NextUDPContainerID();
                            int bufOff = 1;
                            for (DataType packet = p; packet != null; packet = udpSendQueue.TryTake(out packet, 0) ? packet : null) {
                                mStream.Position = 0;
                                Data.Write(bufWriter, packet);
                                bufWriter.Flush();
                                int packLen = (int) mStream.Position;

                                // Copy packet data to the container buffer
                                if (bufOff + packLen > dgBuffer.Length) {
                                    // Send container & start a new one
                                    udpSendSocket.Send(dgBuffer, bufOff, SocketFlags.None);
                                    dgBuffer[0] = NextUDPContainerID();
                                    bufOff = 1;
                                }

                                Buffer.BlockCopy(mStream.GetBuffer(), 0, dgBuffer, bufOff, packLen);
                                bufOff += packLen;

                                if (!(packet is DataLowLevelKeepAlive))
                                    SurpressUDPKeepAlives();
                            }

                            // Send the last container
                            if (bufOff > 1)
                                udpSendSocket.Send(dgBuffer, bufOff, SocketFlags.None);
                        }
                    } catch (Exception e) {
                        Logger.Log(LogLevel.WRN, "udpsend", $"Error in UDP sending thread: {e}");
                        DecreaseUDPScore();
                    }
                }
            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == tokenSrc.Token)
                    return;

                Logger.Log(LogLevel.WRN, "udpsend", $"Error in UDP sending thread: {e}");
                DisposeSafe();
            }
        }

        private static void FlushTCPQueue(CelesteNetSendQueue queue) {
            CelesteNetClientTCPUDPConnection con = (CelesteNetClientTCPUDPConnection) queue.Con;
            foreach (DataType packet in queue.BackQueue)
                con.tcpSendQueue.Add(packet);
            queue.SignalFlushed();
        }

        private static void FlushUDPQueue(CelesteNetSendQueue queue) {
            CelesteNetClientTCPUDPConnection con = (CelesteNetClientTCPUDPConnection) queue.Con;
            foreach (DataType packet in queue.BackQueue)
                con.udpSendQueue.Add(packet);
            queue.SignalFlushed();
        }

    }
}