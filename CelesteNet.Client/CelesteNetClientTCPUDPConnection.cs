using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientTCPUDPConnection : CelesteNetTCPUDPConnection {

        public const int TCPBufferSize = 16384;
        public const int UDPEstablishDelay = 1;

        private BufferedSocketStream tcpStream;
        private ManualResetEventSlim udpReadyEvent;
        private int udpEstablishDelay = -1;
        private BlockingCollection<DataType> tcpSendQueue, udpSendQueue;

        private CancellationTokenSource tokenSrc;
        private Thread tcpRecvThread, udpRecvThread, tcpSendThread, udpSendThread;

        public CelesteNetClientTCPUDPConnection(DataContext data, int token, int maxPacketSize, float mergeWindow, Socket tcpSock) : base(data, token, $"tcpudp-srv-{BitConverter.ToString(((IPEndPoint) tcpSock.RemoteEndPoint).Address.GetAddressBytes())}-prt-{((IPEndPoint) tcpSock.RemoteEndPoint).Port}", maxPacketSize, int.MaxValue, mergeWindow, tcpSock, FlushTCPQueue, FlushUDPQueue) {
            // Initialize networking
            tcpSock.Blocking = false;
            tcpStream = new BufferedSocketStream(TCPBufferSize) { Socket = tcpSock };
            udpReadyEvent = new ManualResetEventSlim(false);
            OnUDPDeath += (_, _) => {
                udpEstablishDelay = UDPEstablishDelay;
                udpReadyEvent.Reset();
                if (CelesteNetClientModule.Instance?.Context?.Status != null)
                    CelesteNetClientModule.Instance.Context.Status.Set(UseUDP ? "UDP connection died" : "Switching to TCP only", 3);
            };
            tcpSendQueue = new BlockingCollection<DataType>();
            udpSendQueue = new BlockingCollection<DataType>();

            // Start threads
            tokenSrc = new CancellationTokenSource();
            tcpRecvThread = new Thread(TCPRecvThreadFunc) { Name = "TCP Recv Thread" };
            udpRecvThread = new Thread(UDPRecvThreadFunc) { Name = "UDP Send Thread" };
            tcpSendThread = new Thread(TCPSendThreadFunc) { Name = "TCP Recv Thread" };
            udpSendThread = new Thread(UDPSendThreadFunc) { Name = "UDP Send Thread" };

            tcpRecvThread.Start();
            udpRecvThread.Start();
            tcpSendThread.Start();
            udpSendThread.Start();
        }

        protected override void Dispose(bool disposing) {
            // Wait for threads
            tokenSrc.Cancel();
            TCPSocket.Shutdown(SocketShutdown.Receive);
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
            tcpSendQueue.Dispose();
            udpSendQueue.Dispose();
        }

        public override bool DoHeartbeatTick() {
            if (base.DoHeartbeatTick())
                return true;

            lock (UDPLock) {
                if (udpEstablishDelay < 0) {}
                else if (udpEstablishDelay == 0) {
                    // If the UDP connection died, try re-establishing it if UDP's not disabled
                    if (UseUDP && UDPEndpoint == null) {
                        // Initialize a UDP connection which isn't established yet
                        InitUDP(TCPSocket.RemoteEndPoint, -1, 0);
                        udpSendQueue.Add(null);
                    }
                } else
                    udpEstablishDelay--;
            }

            return false;
        }

        public override void TCPHeartbeat() {
            base.TCPHeartbeat();

            // Establish the UDP connection after we received the first TCP heartbeat
            lock (UDPLock) {
                if (udpEstablishDelay < 0)
                    udpEstablishDelay = 0;
            }
        }

        private void TCPRecvThreadFunc() {
            try {
                byte[] packetBuffer = new byte[MaxPacketSize];
                using (BinaryReader tcpReader = new BinaryReader(tcpStream, Encoding.UTF8, true)) {
                    while (!tokenSrc.IsCancellationRequested) {
                        // Read the packet
                        UInt16 packetSize = tcpReader.ReadUInt16();
                        if (packetSize > MaxPacketSize)
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
                                lock (UDPLock) {
                                    HandleUDPInfo(udpInfo);
                                    if (UseUDP && UDPEndpoint != null && UDPConnectionID > 0)
                                        udpReadyEvent.Set();
                                }
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
                    Dispose();
                }
                return;
            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == tokenSrc.Token)
                    return;

                if ((e is IOException || e is SocketException) && tokenSrc.IsCancellationRequested)
                    return;

                Logger.Log(LogLevel.WRN, "tcprecv", $"Error in TCP receiving thread: {e}");
                if (!tokenSrc.IsCancellationRequested)
                    Dispose();
            }
        }

        private void UDPRecvThreadFunc() {
            Socket sock = null;
            try {
                byte[] buffer = null;

                // Dispose the socket when the connection dies, to stop listenting for datagrams.
                OnUDPDeath += (_,_) => Interlocked.Exchange(ref sock, null)?.Dispose();
                using (tokenSrc.Token.Register(() => Interlocked.Exchange(ref sock, null)?.Dispose()))
                while (!tokenSrc.IsCancellationRequested) {
                    // Wait until UDP is ready
                    udpReadyEvent.Wait(tokenSrc.Token);

                    lock (UDPLock) {
                        if (UDPEndpoint == null || UDPConnectionID < 0)
                            continue;

                        // Adjust the datagram buffer size
                        if (buffer == null || buffer.Length != UDPMaxDatagramSize)
                            Array.Resize(ref buffer, UDPMaxDatagramSize);

                        // Create the receiving socket
                        if (sock == null) {
                            sock = new Socket(SocketType.Dgram, ProtocolType.Udp);
                            sock.Connect(UDPEndpoint);
                        }
                    }

                    try {
                        // Receive a datagram
                        int dgSize;
                        try {
                            dgSize = sock.Receive(buffer);
                        } catch (SocketException) {
                            if (sock == null)
                                continue;
                            throw;
                        }
                        if (dgSize == 0)
                            continue;

                        // Let the connection now we got a UDP heartbeat
                        // FIXME Heartbeats don't happen -> no packets get through
                        Logger.Log(LogLevel.INF, "udp", "Heartbeat");
                        UDPHeartbeat();

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
                        Logger.Log(LogLevel.WRN, "udpsend", $"Error in UDP sending thread: {e}");
                        DecreaseUDPScore();
                    }
                }
            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == tokenSrc.Token)
                    return;

                Logger.Log(LogLevel.WRN, "udprecv", $"Error in UDP receiving thread: {e}");
                if (!tokenSrc.IsCancellationRequested)
                    Dispose();
            } finally {
                sock?.Dispose();
            }
        }

        private void TCPSendThreadFunc() {
            try {
                using (BinaryWriter tcpWriter = new BinaryWriter(tcpStream, Encoding.UTF8, true))
                using (MemoryStream mStream = new MemoryStream(MaxPacketSize))
                using (CelesteNetBinaryWriter bufWriter = new CelesteNetBinaryWriter(Data, Strings, SlimMap, mStream))
                foreach (DataType p in tcpSendQueue.GetConsumingEnumerable(tokenSrc.Token)) {
                    // Try to send as many packets as possible
                    for (DataType packet = p; packet != null; tcpSendQueue.TryTake(out packet)) {
                        mStream.Position = 0;
                        int packLen = Data.Write(bufWriter, packet);
                        tcpWriter.Write((UInt16) packLen);
                        tcpWriter.Write(mStream.GetBuffer(), 0, packLen);
                        SurpressTCPKeepAlives();
                    }
                    tcpWriter.Flush();
                }
            } catch (Exception e) {
                if (e is OperationCanceledException oe && oe.CancellationToken == tokenSrc.Token)
                    return;

                Logger.Log(LogLevel.WRN, "tcpsend", $"Error in TCP sending thread: {e}");
                if (!tokenSrc.IsCancellationRequested)
                    Dispose();
            }
        }

        private void UDPSendThreadFunc() {
            try {
                byte[] dgBuffer = null;
                using (MemoryStream mStream = new MemoryStream(MaxPacketSize))
                using (CelesteNetBinaryWriter bufWriter = new CelesteNetBinaryWriter(Data, Strings, SlimMap, mStream))
                using (Socket sock = new Socket(SocketType.Dgram, ProtocolType.Udp))
                foreach (DataType p in udpSendQueue.GetConsumingEnumerable(tokenSrc.Token)) {
                    try {
                        lock (UDPLock) {
                            EndPoint ep = UDPEndpoint;
                            if (ep == null || UDPMaxDatagramSize <= 0) {
                                if (!UseUDP)
                                    return;

                                // Try to establish the UDP connection
                                // If it succeeeds, we'll receive a udpInfo packet with our connection parameters
                                if (p == null)
                                    sock.SendTo(BitConverter.GetBytes(ConnectionToken), 4, SocketFlags.None, TCPSocket.RemoteEndPoint);

                                continue;
                            } else if (p == null)
                                continue;

                            // Adjust the datagram buffer size
                            if (dgBuffer == null || dgBuffer.Length != UDPMaxDatagramSize)
                                Array.Resize(ref dgBuffer, UDPMaxDatagramSize);

                            // Try to send as many packets as possible
                            dgBuffer[0] = NextUDPContainerID();
                            int bufOff = 1;
                            for (DataType packet = p; packet != null; packet = udpSendQueue.TryTake(out packet, 0) ? packet : null) {
                                mStream.Position = 0;
                                int packLen = Data.Write(bufWriter, packet);
                                if (!(packet is DataLowLevelKeepAlive))
                                    SurpressUDPKeepAlives();

                                // Copy packet data to the container buffer
                                if (bufOff + packLen > dgBuffer.Length) {
                                    // Send container & start a new one
                                    sock.SendTo(dgBuffer, bufOff, SocketFlags.None, UDPEndpoint!);
                                    dgBuffer[0] = NextUDPContainerID();
                                    bufOff = 1;
                                }

                                Buffer.BlockCopy(mStream.GetBuffer(), 0, dgBuffer, bufOff, packLen);
                                bufOff += packLen;
                            }

                            // Send the last container
                            if (bufOff > 1)
                                sock.SendTo(dgBuffer, bufOff, SocketFlags.None, ep);
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
                if (!tokenSrc.IsCancellationRequested)
                    Dispose();
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