using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Celeste.Mod.CelesteNet.Server {
    public class TCPUDPServer : IDisposable {

        private const int SOL_SOCKET = 1, SO_REUSEPORT = 15;
        [DllImport("libc", SetLastError = true)] 
        private static extern int setsockopt(IntPtr socket, int level, int opt, [In, MarshalAs(UnmanagedType.LPArray)] int[] val, int len);

        public readonly CelesteNetServer Server;

        protected TcpListener? TCPListener;
        protected UdpClient?[] UDPs;

        private Thread? TCPListenerThread;
        private Thread?[] UDPReadThreads;

        private uint UDPNextID = (uint) (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);
        private readonly ConcurrentDictionary<CelesteNetTCPUDPConnection, UDPPendingKey> UDPKeys = new();
        private readonly ConcurrentDictionary<UDPPendingKey, CelesteNetTCPUDPConnection> UDPPending = new();
        private readonly ConcurrentDictionary<IPEndPoint, CelesteNetTCPUDPConnection> UDPMap = new();

        public TCPUDPServer(CelesteNetServer server) {
            Server = server;
            Server.Data.RegisterHandlersIn(this);
        }

        public void Start() {
            Logger.Log(LogLevel.CRI, "tcpudp", $"Startup on port {Server.Settings.MainPort}");

            TCPListener = new(IPAddress.IPv6Any, Server.Settings.MainPort);
            TCPListener.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
            TCPListener.Start();

            TCPListenerThread = new(TCPListenerLoop) {
                Name = $"TCPUDPServer TCPListener ({GetHashCode()})",
                IsBackground = true
            };
            TCPListenerThread.Start();

            int numUdpThreads = Server.Settings.NumUDPThreads;
            if (numUdpThreads < 0) {
                // Determine suitable number of UDP threads
                // On Windows, having multiple threads isn't an advantage at all, so start spawn one
                // On Linux/MacOS, spawn one thread for each logical core
                if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX) 
                    numUdpThreads = 1; 
                else 
                    numUdpThreads = Environment.ProcessorCount;
            }
            Logger.Log(LogLevel.CRI, "tcpudp", $"Starting {numUdpThreads} UDP threads");

            UDPs = new UdpClient?[numUdpThreads];
            UDPReadThreads = new Thread?[numUdpThreads];
            for (int i = 0; i < numUdpThreads; i++) {
                Socket udpSocket = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                udpSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);

                if (numUdpThreads > 1) {
                    udpSocket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.ReuseAddress, true);

                    // Some Linux/MacOS runtimes don't set SO_REUSPORT (the option we care about), so set that explicitly
                    if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX) {
                        if (setsockopt(udpSocket.Handle, SOL_SOCKET, SO_REUSEPORT, new[] { 1 }, sizeof(int)) < 0) {
                            // Even though the method is named GetLastWin32Error, it still works on other platforms
                            // NET 6.0 added the better named method GetLastPInvokeError, which does the same thing
                            // However, still use GetLastWin32Error to remain compatible with the net452 build target
                            Logger.Log(LogLevel.WRN, "tcpudp", $"Failed enabling UDP socket option SO_REUSEPORT for socket {i}: {Marshal.GetLastWin32Error()}");
                        }
                    } else if (i == 0)
                        Logger.Log(LogLevel.WRN, "tcpudp", "Starting more than one UDP thread on a platform without SO_REUSEPORT!");
                }

                udpSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, Server.Settings.MainPort));
                UDPs[i] = new(AddressFamily.InterNetworkV6);
                UDPs[i].Client.Dispose();
                UDPs[i].Client = udpSocket;

                // This is neccessary, as otherwise i could increment before the thread function is called
                int idx = i;
                UDPReadThreads[i] = new(() => UDPReadLoop(idx)) {
                    Name = $"TCPUDPServer UDPRead {i} ({GetHashCode()})",
                    IsBackground = true
                };
                UDPReadThreads[i].Start();
            }
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "tcpudp", "Shutdown");

            TCPListener?.Stop();
            foreach (UdpClient? UDP in UDPs) UDP?.Close();

            Server.Data.UnregisterHandlersIn(this);
        }

        protected virtual void TCPListenerLoop() {
            try {
                while (Server.IsAlive && TCPListener != null) {
                    TcpClient client = TCPListener.AcceptTcpClient();
                    CelesteNetTCPUDPConnection? con = null;
                    try {
                        if (client.Client.RemoteEndPoint is not IPEndPoint rep)
                            continue;
                        Logger.Log(LogLevel.VVV, "tcpudp", $"New TCP connection: {rep}");

                        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 6000);

                        con = new(Server.Data, client, null);
                        uint token;
                        lock (UDPPending)
                            token = UDPNextID++;
                        UDPPendingKey key = new() {
                            IPHash = rep.Address.GetHashCode(),
                            Token = token
                        };
                        UDPKeys[con] = key;
                        UDPPending[key] = con;
                        con.OnDisconnect += RemoveUDPPending;
                        con.Send(new DataTCPHTTPTeapot() {
                            ConnectionFeatures = Server.ConnectionFeatures,
                            ConnectionToken = token
                        });
                        con.StartReadTCP();
                        Server.HandleConnect(con);

                    } catch (ThreadAbortException) {

                    } catch (Exception e) {
                        Logger.Log(LogLevel.CRI, "tcpudp", $"Failed handling TCP connection:\n{e}");
                        con?.Dispose();
                    }
                }

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                Logger.Log(LogLevel.CRI, "tcpudp", $"Failed listening for TCP connection:\n{e}");
                Server.Dispose();
            }
        }

        protected virtual void UDPReadLoop(int idx) {
            try {
                using MemoryStream stream = new();
                using CelesteNetBinaryReader reader = new(Server.Data, null, stream);
                while (Server.IsAlive && UDPs[idx] != null) {
                    IPEndPoint? remote = null;
                    byte[] raw;
                    try {
                        raw = UDPs[idx].Receive(ref remote);
                    } catch (SocketException) {
                        continue;
                    }

                    if (remote == null)
                        continue;

                    if (!UDPMap.TryGetValue(remote, out CelesteNetTCPUDPConnection? con)) {
                        if (raw.Length == 4) {
                            UDPPendingKey key = new() {
                                IPHash = remote.Address.GetHashCode(),
                                Token = BitConverter.ToUInt32(raw, 0)
                            };
                            if (UDPPending.TryRemove(key, out con)) {
                                Logger.Log(LogLevel.CRI, "tcpudp", $"New UDP connection on thread {idx}: {remote}");
                                con.OnDisconnect -= RemoveUDPPending;

                                con.UDP = UDPs[idx];
                                con.UDPLocalEndPoint = (IPEndPoint?) UDPs[idx].Client.LocalEndPoint;
                                con.UDPRemoteEndPoint = remote;

                                UDPMap[con.UDPRemoteEndPoint] = con;
                                con.OnDisconnect += RemoveUDPMap;
                                continue;
                            }
                        }

                        if (con == null)
                            continue;
                    }

                    try {
                        reader.Strings = con.UDPQueue.Strings;

                        stream.Seek(0, SeekOrigin.Begin);
                        stream.Write(raw, 0, raw.Length);

                        stream.Seek(0, SeekOrigin.Begin);
                        Server.Data.Handle(con, Server.Data.Read(reader));
                    } catch (Exception e) {
                        Logger.Log(LogLevel.CRI, "tcpudp", $"Failed handling UDP data on thread {idx}, {raw.Length} bytes:\n{con}\n{e}");
                        // Sometimes we receive garbage via UDP. Oh well...
                        Handle(con, new DataTCPOnlyDowngrade());
                    }
                }

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                Logger.Log(LogLevel.CRI, "tcpudp", $"Failed waiting for UDP data on thread {idx}:\n{e}");
                Server.Dispose();
            }
        }

        private void RemoveUDPPending(CelesteNetConnection _con) {
            CelesteNetTCPUDPConnection con = (CelesteNetTCPUDPConnection) _con;
            if (UDPKeys.TryRemove(con, out UDPPendingKey key))
                UDPPending.TryRemove(key, out _);
        }

        private void RemoveUDPMap(CelesteNetConnection _con) {
            CelesteNetTCPUDPConnection con = (CelesteNetTCPUDPConnection) _con;
            UDPKeys.TryRemove(con, out _);
            IPEndPoint? ep = con.UDPRemoteEndPoint;
            if (ep != null)
                UDPMap.TryRemove(ep, out _);
        }

        public event Action<CelesteNetTCPUDPConnection, string>? OnInitConnectionFeature;

        public void InitConnectionFeature(CelesteNetTCPUDPConnection con, string feature) {
            switch (feature) {
                case StringMap.ConnectionFeature:
                    con.SendStringMap = true;
                    break;
            }

            OnInitConnectionFeature?.Invoke(con, feature);
        }


        #region Handlers

        public void Handle(CelesteNetTCPUDPConnection con, DataHandshakeTCPUDPClient handshake) {
            if (handshake.Version != CelesteNetUtils.Version) {
                con.Send(new DataDisconnectReason { Text = "Protocol version mismatch" });
                con.Send(new DataInternalDisconnect());
                return;
            }

            // FIXME: Possible race condition on rehandshake after disconnect?
            if (Server.PlayersByCon.ContainsKey(con))
                return;

            foreach (string feature in handshake.ConnectionFeatures)
                InitConnectionFeature(con, feature);

            CelesteNetPlayerSession session = new(Server, con, ++Server.PlayerCounter);
            session.Start(handshake);
        }

        public void Handle(CelesteNetTCPUDPConnection con, DataTCPOnlyDowngrade downgrade) {
            con.UDP = null;
            con.UDPLocalEndPoint = null;
            IPEndPoint? ep = con.UDPRemoteEndPoint;
            con.UDPRemoteEndPoint = null;

            if (UDPKeys.TryGetValue(con, out UDPPendingKey key))
                UDPPending[key] = con;

            if (ep != null)
                UDPMap.TryRemove(ep, out _);

            con.Send(downgrade);
        }

        #endregion

        private struct UDPPendingKey {
            public int IPHash;
            public uint Token;
        }

    }
}
