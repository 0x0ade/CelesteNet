﻿using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;
using System;
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

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClient : IDisposable {

        public readonly CelesteNetClientSettings Settings;

        public readonly DataContext Data;

        public CelesteNetConnection Con;
        public readonly IConnectionFeature[] ConFeatures;
        public volatile bool SafeDisposeTriggered = false;

        private bool _IsAlive;
        public bool IsAlive {
            get => _IsAlive;
            set {
                if (_IsAlive == value)
                    return;

                _IsAlive = value;
            }
        }

        public bool IsReady { get; protected set; }
        private readonly ManualResetEventSlim _ReadyEvent;

        public DataPlayerInfo PlayerInfo;

        private readonly object StartStopLock = new();

        private System.Timers.Timer HeartbeatTimer;

        public CelesteNetClient()
            : this(new()) {
        }

        public CelesteNetClient(CelesteNetClientSettings settings) {
            Settings = settings;

            Data = new();
            Data.RegisterHandlersIn(this);

            _ReadyEvent = new(false);

            // Find connection features
            List<IConnectionFeature> conFeatures = new();
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                try {
                    if (!typeof(IConnectionFeature).IsAssignableFrom(type) || type.IsAbstract)
                        continue;
                } catch {
                    continue;
                }

                IConnectionFeature feature = (IConnectionFeature) Activator.CreateInstance(type);
                if (feature == null)
                    throw new Exception($"Cannot create instance of connection feature {type.FullName}");
                Logger.Log(LogLevel.VVV, "main", $"Found connection feature: {type.FullName}");
                conFeatures.Add(feature);
            }
            ConFeatures = conFeatures.ToArray();
        }

        public void Start(CancellationToken token) {
            if (IsAlive)
                return;
            IsAlive = true;

            lock (StartStopLock) {
                if (IsReady)
                    return;
                Logger.Log(LogLevel.CRI, "main", $"Startup");

                switch (Settings.ConnectionType) {
                    case ConnectionType.Auto:
                    case ConnectionType.TCPUDP:
                    case ConnectionType.TCP:
                        Logger.Log(LogLevel.INF, "main", $"Connecting via TCP/UDP to {Settings.Host}:{Settings.Port}");

                        // Create a TCP connection
                        // The socket connection code is roughly based off of the TCPClient reference source.
                        // Let's avoid dual mode sockets as there seem to be bugs with them on Mono.
                        List<Socket> sockAll = new();
                        if (Socket.OSSupportsIPv6)
                            sockAll.Add(new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp));
                        if (Socket.OSSupportsIPv4)
                            sockAll.Add(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp));
                        Socket sock = null;
                        try {
                            uint conToken;
                            IConnectionFeature[] conFeatures;
                            CelesteNetTCPUDPConnection.Settings settings;
                            using (token.Register(() => {
                                foreach (Socket sockTry in sockAll) {
                                    try {
                                        sockTry.Close();
                                    } catch {
                                    }
                                    sockTry.Dispose();
                                }
                            })) {
                                Exception sockEx = null;
                                IPAddress[] addresses = Dns.GetHostAddresses(Settings.Host);
                                Tuple<uint, IConnectionFeature[], CelesteNetTCPUDPConnection.Settings> teapotRes = null;

                                foreach (Socket sockTry in sockAll)
                                    Logger.Log(LogLevel.DBG, "main", $"... with socket type {sockTry.AddressFamily}");

                                foreach (IPAddress address in addresses)
                                    Logger.Log(LogLevel.DBG, "main", $"... to address {address} ({address.AddressFamily})");

                                // Try IPv6 first if possible, then try IPv4.
                                foreach (Socket sockTry in sockAll) {
                                    foreach (IPAddress address in addresses) {
                                        if (sockTry.AddressFamily != address.AddressFamily)
                                            continue;
                                        try {
                                            sock = sockTry;
                                            sock.ReceiveTimeout = sock.SendTimeout = 2000;
                                            sock.Connect(address, Settings.Port);
                                            // Do the teapot handshake here, as a successful "connection" doesn't mean that the server can handle IPv6.
                                            teapotRes = Handshake.DoTeapotHandshake<CelesteNetClientTCPUDPConnection.Settings>(sock, ConFeatures, Settings.Name);
                                            Logger.Log(LogLevel.INF, "main", $"Connecting to {address} ({address.AddressFamily}) succeeded");
                                            break;
                                        } catch (Exception e) {
                                            Logger.Log(LogLevel.INF, "main", $"Connecting to {address} ({address.AddressFamily}) failed: {e.GetType()}: {e.Message}");
                                            sock?.ShutdownSafe(SocketShutdown.Both);
                                            sock = null;
                                            teapotRes = null;
                                            sockEx = e;
                                            continue;
                                        }
                                    }
                                    if (sock != null)
                                        break;
                                }

                                // Cleanup all non-used sockets.
                                foreach (Socket sockTry in sockAll) {
                                    if (sockTry == sock)
                                        continue;
                                    try {
                                        sockTry.Close();
                                    } catch {
                                    }
                                    sockTry.Dispose();
                                }

                                if (sock == null || teapotRes == null) {
                                    if (sockEx == null) {
                                        throw new Exception($"Failed to connect to {Settings.Host}:{Settings.Port}, didn't find any connectable address, no exception (was any address even tried?)");
                                    }
                                    if (sockEx is SocketException sockExSock) {
                                        if (sockExSock.SocketErrorCode == SocketError.WouldBlock || sockExSock.SocketErrorCode == SocketError.TimedOut) {
                                            throw new Exception($"Failed to connect to {Settings.Host}:{Settings.Port}, didn't find any connectable address, last tried address timed out");
                                        }
                                    }
                                    throw new Exception($"Failed to connect to {Settings.Host}:{Settings.Port}, didn't find any connectable address", sockEx);
                                }

                                // Process the teapot handshake
                                conToken = teapotRes.Item1;
                                conFeatures = teapotRes.Item2;
                                settings = teapotRes.Item3;
                                Logger.Log(LogLevel.INF, "main", $"Teapot handshake success: token {conToken} conFeatures '{conFeatures.Select(f => f.GetType().FullName).Aggregate((string) null, (a, f) => (a == null) ? f : $"{a}, {f}")}'");
                                sock.ReceiveTimeout = sock.SendTimeout = (int) (settings.HeartbeatInterval * settings.MaxHeartbeatDelay);
                            }

                            // Create a connection and start the heartbeat timer
                            CelesteNetClientTCPUDPConnection con = new(this, conToken, settings, sock);
                            con.OnDisconnect += _ => {
                                CelesteNetClientModule.Instance.Context?.Dispose();
                                Dispose();
                                CelesteNetClientModule.Instance.Context?.Status?.Set("Server disconnected", 3f);
                            };
                            if (Settings.ConnectionType == ConnectionType.TCP)
                                con.UseUDP = false;

                            // Initialize the heartbeat timer
                            HeartbeatTimer = new(settings.HeartbeatInterval);
                            HeartbeatTimer.AutoReset = true;
                            HeartbeatTimer.Elapsed += (_, _) => {
                                string disposeReason = con.DoHeartbeatTick();
                                if (disposeReason != null) {
                                    Logger.Log(LogLevel.CRI, "main", disposeReason);
                                    Dispose();
                                }
                            };
                            HeartbeatTimer.Start();

                            // Do the regular connection handshake
                            Handshake.DoConnectionHandshake(Con, conFeatures, token);
                            Logger.Log(LogLevel.INF, "main", $"Connection handshake success");

                            Con = con;
                        } catch {
                            Con?.Dispose();
                            foreach (Socket sockTry in sockAll) {
                                try {
                                    sockTry.Close();
                                } catch {
                                }
                                sockTry.Dispose();
                            }
                            Con = null;
                            throw;
                        }

                        break;

                    default:
                        throw new NotSupportedException($"Unsupported connection type {Settings.ConnectionType}");
                }
            }

            // Wait until the server sent the ready packet
            _ReadyEvent.Wait(token);
            SendFilterList();

            Logger.Log(LogLevel.INF, "main", "Ready");
            IsReady = true;
        }

        public void Dispose() {
            if (!IsAlive)
                return;
            IsAlive = false;

            lock (StartStopLock) {
                if (Con == null)
                    return;
                Logger.Log(LogLevel.CRI, "main", "Shutdown");
                IsReady = false;

                HeartbeatTimer?.Dispose();
                Con?.Dispose();
                Con = null;

                Data.Dispose();
                _ReadyEvent?.Dispose();
            }
        }

        public void SendFilterList() {
            Logger.Log(LogLevel.INF, "main", "Sending filter list");
            Con?.Send(new DataNetFilterList {
                List = Data.DataTypeToSource.Values.Distinct().ToArray()
            });
        }


        public DataType Send(DataType data) {
            Con?.Send(data);
            return data;
        }

        public T Send<T>(T data) where T : DataType<T> {
            Con?.Send(data);
            return data;
        }

        public DataType SendAndHandle(DataType data) {
            Con?.Send(data);
            Data.Handle(null, data);
            return data;
        }

        public T SendAndHandle<T>(T data) where T : DataType<T> {
            Con?.Send(data);
            Data.Handle(null, data);
            return data;
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo info) {
            // The first DataPlayerInfo sent from the server is our own
            if (PlayerInfo == null || PlayerInfo.ID == info.ID)
                PlayerInfo = info;
        }

        public void Handle(CelesteNetConnection con, DataReady ready) {
            _ReadyEvent.Set();
        }

    }
}
