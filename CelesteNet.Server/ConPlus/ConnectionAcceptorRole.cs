using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public partial class ConnectionAcceptorRole : MultipleSocketBinderRole {

        public const int MAX_WORKER_BACKLOG = 32;

        private class Worker : RoleWorker {

            public Worker(ConnectionAcceptorRole role, NetPlusThread thread) : base(role, thread) {}

            protected override void StartWorker(Socket socket, CancellationToken token) {
                EnterActiveZone();

                // Accept new connections as long as the token isn't canceled
                socket.Listen(MAX_WORKER_BACKLOG);
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

                    // Start the connection handshake
                    EndPoint remoteEP = newConn.RemoteEndPoint!;
                    Logger.Log(LogLevel.VVV, "conaccpt", $"Incoming connection from {remoteEP} <-> {Role.EndPoint}");
                    Role.Handshaker.Factory.StartNew(() => {
                        Role.Handshaker.DoSocketHandshake(newConn).ContinueWith(t => {
                            if (t.IsFaulted)
                                Logger.Log(LogLevel.WRN, "conaccpt", $"Handshake failed for connection {remoteEP}: {t.Exception}");
                        });
                    });
                }
                ExitActiveZone();
            }

            public new ConnectionAcceptorRole Role => (ConnectionAcceptorRole) base.Role;

        }

        public ConnectionAcceptorRole(NetPlusThreadPool pool, EndPoint endPoint, HandshakerRole handshaker) : base(pool, ProtocolType.Tcp, endPoint) {
            Handshaker = handshaker;
        }

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

        public HandshakerRole Handshaker { get; }

    }
}