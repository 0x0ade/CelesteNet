using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public partial class TCPAcceptorRole : MultipleSocketBinderRole {

        public const int MaxWorkerBacklog = 32;

        private class Worker : RoleWorker {

            public new TCPAcceptorRole Role => (TCPAcceptorRole) base.Role;

            public Worker(TCPAcceptorRole role, NetPlusThread thread) : base(role, thread) {}

            protected override void StartWorker(Socket socket, CancellationToken token) {
                EnterActiveZone();

                // Accept new connections as long as the token isn't canceled
                socket.Listen(MaxWorkerBacklog);
                token.Register(() => socket.Close());
                while (!token.IsCancellationRequested) {
                    Socket newCon;

                    ExitActiveZone();
                    try {
                        newCon = socket.Accept();
                    } catch (SocketException) {
                        // There's no better way to do this, as far as I know...
                        if (token.IsCancellationRequested)
                            return;
                        throw;
                    }
                    EnterActiveZone();

                    // Setup the new socket
                    newCon.ReceiveBufferSize = Role.Server.Settings.TCPRecvBufferSize;
                    newCon.SendBufferSize = Role.Server.Settings.MaxQueueSize * (2 + Role.Server.Settings.MaxPacketSize);

                    // Start the connection handshake
                    EndPoint remoteEP = newCon.RemoteEndPoint!;
                    Logger.Log(LogLevel.VVV, "tcpaccept", $"Incoming connection from {remoteEP} <-> {Role.EndPoint}");
                    Role.Handshaker.Factory.StartNew(() => {
                        Role.Handshaker.DoTCPUDPHandshake(newCon, Role.ConnectionSettings, Role.TCPReceiver, Role.UDPReceiver, Role.Sender).ContinueWith(t => {
                            if (t.IsFaulted) {
                                if (t.Exception?.InnerException is SocketException se && se.IsDisconnect())
                                    Logger.Log(LogLevel.WRN, "tcpaccept", $"Disconnect during handshake for connection {remoteEP}");
                                else
                                    Logger.Log(LogLevel.WRN, "tcpaccept", $"Handshake failed for connection {remoteEP}: {t.Exception}");
                            }
                        });
                    });
                }
                ExitActiveZone();
            }

        }

        public CelesteNetServer Server { get; }
        public HandshakerRole Handshaker { get; }
        public TCPReceiverRole TCPReceiver { get; }
        public UDPReceiverRole UDPReceiver { get; }
        public TCPUDPSenderRole Sender { get; }

        public CelesteNetTCPUDPConnection.Settings ConnectionSettings { get; }

        public TCPAcceptorRole(
            NetPlusThreadPool pool,
            CelesteNetServer server,
            EndPoint endPoint,
            HandshakerRole handshaker,
            TCPReceiverRole tcpReceiver,
            UDPReceiverRole udpReceiver,
            TCPUDPSenderRole sender,
            CelesteNetTCPUDPConnection.Settings conSettings
        ) : base(pool, ProtocolType.Tcp, endPoint) {
            Server = server;
            Handshaker = handshaker;
            TCPReceiver = tcpReceiver;
            UDPReceiver = udpReceiver;
            Sender = sender;
            ConnectionSettings = conSettings;
        }

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

    }
}