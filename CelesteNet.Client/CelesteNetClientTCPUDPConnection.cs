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

        public const int TcpBufferSize = 16384;

        // TODO MonoKickstart is extremly stupid, using a BufferedStream here just hangs...
        private BufferedSocketStream tcpStream;
        private ManualResetEventSlim udpReadyEvent;
        private BlockingCollection<DataType> tcpSendQueue, udpSendQueue;

        private CancellationTokenSource tokenSrc;
        private Thread tcpRecvThread, udpRecvThread, tcpSendThread, udpSendThread;

        public CelesteNetClientTCPUDPConnection(DataContext data, int token, int maxPacketSize, float mergeWindow, Socket tcpSock) : base(data, token, $"tcpudp-srv-{BitConverter.ToString(((IPEndPoint) tcpSock.RemoteEndPoint).Address.GetAddressBytes())}-prt-{((IPEndPoint) tcpSock.RemoteEndPoint).Port}", maxPacketSize, int.MaxValue, mergeWindow, tcpSock, FlushTCPQueue, FlushUDPQueue) {
            // Initialize networking
            tcpSock.Blocking = false;
            tcpStream = new BufferedSocketStream(TcpBufferSize) { Socket = tcpSock };
            udpReadyEvent = new ManualResetEventSlim(false);
            OnUDPDeath += (_, _) => {
                udpReadyEvent.Reset();
                udpSendQueue.Add(null);
            };
            tcpSendQueue = new BlockingCollection<DataType>();
            udpSendQueue = new BlockingCollection<DataType>();
            udpSendQueue.Add(null);

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
            tokenSrc.Cancel();
            base.Dispose(disposing);

            // Dispose stuff
            tcpSendQueue.CompleteAdding();
            udpSendQueue.CompleteAdding();
            tcpStream.Dispose();

            // Wait for threads
            if (Thread.CurrentThread != tcpRecvThread)
                tcpRecvThread.Join();
            if (Thread.CurrentThread != udpRecvThread)
                udpRecvThread.Join();
            if (Thread.CurrentThread != tcpSendThread)
                tcpSendThread.Join();
            if (Thread.CurrentThread != udpSendThread)
                udpSendThread.Join();

            tokenSrc.Dispose();
            tcpSendQueue.Dispose();
            udpSendQueue.Dispose();
        }

        private void TCPRecvThreadFunc() {
            try {
                using (CelesteNetBinaryReader tcpReader = new CelesteNetBinaryReader(Data, Strings, SlimMap, tcpStream, true)) {
                    while (!tokenSrc.IsCancellationRequested) {
                        // Read the packet size
                        UInt16 packetSize = tcpReader.ReadUInt16();
                        if (packetSize > MaxPacketSize)
                            throw new InvalidDataException("Peer sent packet over maximum size");

                        // Let the connection now we got a TCP heartbeat
                        TCPHeartbeat();

                        // Read the packet
                        DataType packet = Data.Read(tcpReader);

                        // Handle the packet
                        switch (packet) {
                            case DataLowLevelUDPInfo udpInfo: {
                                if (UseUDP && UDPEndpoint == null && udpInfo.MaxDatagramSize > 0) {
                                    InitUDP(TCPSocket.RemoteEndPoint, udpInfo.MaxDatagramSize);
                                    udpReadyEvent.Set();
                                } else
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
                while (!tokenSrc.IsCancellationRequested) {
                    // Wait until UDP is ready
                    udpReadyEvent.Wait(tokenSrc.Token);

                    lock (UDPLock) {
                        if (UDPEndpoint == null)
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

                    // Receive a datagram
                    int dgSize;
                    try {
                        dgSize = sock.Receive(buffer, buffer.Length, SocketFlags.None);
                    } catch (SocketException) {
                        if (sock == null)
                            continue;
                        throw;
                    }
                    if (dgSize == 0)
                        continue;

                    // Let the connection now we got a UDP heartbeat
                    UDPHeartbeat();

                    // Get the container ID
                    byte containerID = buffer[0];

                    // Read packets until we run out data
                    using (MemoryStream mStream = new MemoryStream(buffer, 1, dgSize-1))
                    using (CelesteNetBinaryReader reader = new CelesteNetBinaryReader(Data, Strings, SlimMap, mStream))
                    while (mStream.Position < dgSize-1) {
                        DataType packet = Data.Read(reader);
                        if (packet.TryGet<MetaOrderedUpdate>(Data, out MetaOrderedUpdate orderedUpdate))
                            orderedUpdate.UpdateID = containerID;
                        Receive(packet);
                    }

                    // Promote optimizations
                    PromoteOptimizations();
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
                byte[] dgramBuffer = null;
                using (MemoryStream mStream = new MemoryStream(MaxPacketSize))
                using (CelesteNetBinaryWriter bufWriter = new CelesteNetBinaryWriter(Data, Strings, SlimMap, mStream))
                using (Socket sock = new Socket(SocketType.Dgram, ProtocolType.Udp))
                foreach (DataType p in udpSendQueue.GetConsumingEnumerable(tokenSrc.Token)) {
                    lock (UDPLock) {
                        EndPoint ep = UDPEndpoint;
                        if (ep == null) {
                            if (!UseUDP)
                                return;
                            
                            if (p == null) {
                                // Try to establish a UDP connection
                                sock.SendTo(BitConverter.GetBytes(ConnectionToken), 4, SocketFlags.None, TCPSocket.RemoteEndPoint);
                            }
                            continue;
                        }

                        // Adjust the datagram buffer size
                        if (dgramBuffer == null || dgramBuffer.Length != UDPMaxDatagramSize)
                            Array.Resize(ref dgramBuffer, UDPMaxDatagramSize);
                            
                        // Try to send as many packets as possible
                        dgramBuffer[0] = NextUDPContainerID();
                        int bufOff = 1;
                        for (DataType packet = p; packet != null; packet = udpSendQueue.TryTake(out packet, 0) ? packet : null) {
                            mStream.Position = 0;
                            int packLen = Data.Write(bufWriter, packet);
                            if (!(packet is DataLowLevelKeepAlive))
                                SurpressUDPKeepAlives();

                            // Copy packet data to the container buffer
                            if (bufOff + packLen > dgramBuffer.Length) {
                                // Send container
                                sock.SendTo(dgramBuffer, bufOff, SocketFlags.None, ep);
                                bufOff = 1;

                                // Start a new container
                                dgramBuffer[0] = NextUDPContainerID();
                            }

                            Buffer.BlockCopy(mStream.GetBuffer(), 0, dgramBuffer, bufOff, packLen);
                            bufOff += packLen;
                        }

                        // Send the last container
                        if (bufOff > 1)
                            sock.SendTo(dgramBuffer, bufOff, SocketFlags.None, ep);
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