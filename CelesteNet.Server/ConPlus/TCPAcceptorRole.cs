using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public partial class TCPAcceptorRole : MultipleSocketBinderRole {

        public const int MaxWorkerBacklog = 32;

        private class Worker : RoleWorker {

            public Worker(TCPAcceptorRole role, NetPlusThread thread) : base(role, thread) {}

            protected override void StartWorker(Socket socket, CancellationToken token) {
                EnterActiveZone();

                // Accept new connections as long as the token isn't canceled
                socket.Listen(MaxWorkerBacklog);
                token.Register(() => socket.Close());
                while (!token.IsCancellationRequested) {
                    Socket newConn;

                    ExitActiveZone();
                    try {
                        newConn = socket.Accept();
                    } catch (SocketException) {
                        // There's no better way to do this, as far as I know...
                        if (token.IsCancellationRequested) 
                            return;
                        throw;
                    }
                    EnterActiveZone();

                    // Setup the new socket
                    newConn.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, Role.Server.Settings.TCPSockSendBufferSize);

                    // Start the connection handshake
                    EndPoint remoteEP = newConn.RemoteEndPoint!;
                    Logger.Log(LogLevel.VVV, "tcpaccept", $"Incoming connection from {remoteEP} <-> {Role.EndPoint}");
                    Role.Handshaker.Factory.StartNew(() => {
                        Role.Handshaker.DoTCPUDPHandshake(newConn, Role.ConnectionSettings, Role.TCPReceiver, Role.UDPReceiver, Role.Sender).ContinueWith(t => {
                            if (t.IsFaulted)
                                Logger.Log(LogLevel.WRN, "tcpaccept", $"Handshake failed for connection {remoteEP}: {t.Exception}");
                        });
                    });
                }
                ExitActiveZone();
            }

            public new TCPAcceptorRole Role => (TCPAcceptorRole) base.Role;

        }

        public TCPAcceptorRole(NetPlusThreadPool pool, CelesteNetServer server, EndPoint endPoint, HandshakerRole handshaker, TCPReceiverRole tcpReceiver, UDPReceiverRole udpReceiver, TCPUDPSenderRole sender, CelesteNetTCPUDPConnection.Settings conSettings) : base(pool, ProtocolType.Tcp, endPoint) {
            Server = server;
            Handshaker = handshaker;
            TCPReceiver = tcpReceiver;
            UDPReceiver = udpReceiver;
            Sender = sender;
            ConnectionSettings = conSettings;
        }

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

        public CelesteNetServer Server { get; }
        public HandshakerRole Handshaker { get; }
        public TCPReceiverRole TCPReceiver { get; }
        public UDPReceiverRole UDPReceiver { get; }
        public TCPUDPSenderRole Sender { get; }

        public CelesteNetTCPUDPConnection.Settings ConnectionSettings { get; }

        /*
        Having more than one thread accepting connections will cause all TCP
        packets to be sent with a TTL of 1. This of course breaks things, so we
        just, don't do that (accepting connections isn't a big bottleneck anyway).
        */
        // TODO Investigate the Linux kernel code for what causes this to happen
        public override int MaxThreads => 1;

    }
}