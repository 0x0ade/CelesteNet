using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public class ConnectionAcceptorRole : MultipleSocketBinderRole {

        public const int MAX_WORKER_BACKLOG = 32;

        private class Worker : RoleWorker {

            public Worker(ConnectionAcceptorRole role, NetPlusThread thread) : base(role, thread) {}

            protected override void StartWorker(Socket socket, CancellationToken token) {
                // Accept new connections as long as the token isn't canceled
                socket.Listen(MAX_WORKER_BACKLOG);
                token.Register(() => socket.Close());
                while (true) {
                    Socket newConn;
                    try {
                        newConn = socket.Accept();
                    } catch (SocketException) {
                        // There's no better way to do this, as far as I know...
                        if (token.IsCancellationRequested) 
                            return;
                        throw;
                    }

                    // Log the connection
                    Logger.Log(LogLevel.VVV, "conaccpt", $"Incoming connection from {newConn.RemoteEndPoint} <-> {Role.EndPoint}");

                    EnterActiveZone();
                    try {
                        AcceptConnection(newConn);
                    } catch (Exception e) {
                        Logger.Log(LogLevel.CRI, "conaccpt", $"Error while accepting connection from {newConn.RemoteEndPoint}: {e}");
                    } finally {
                        ExitActiveZone();
                    }
                }
            }

            private void AcceptConnection(Socket sock) {
                using (sock) {
                    sock.Send(System.Text.Encoding.ASCII.GetBytes($"Hello World! Current thread index: {Thread.Index}\n"));
                }
            }
        
            public new ConnectionAcceptorRole Role => (ConnectionAcceptorRole) base.Role;

        }

        public ConnectionAcceptorRole(NetPlusThreadPool pool, EndPoint endPoint) : base(pool, ProtocolType.Tcp, endPoint) {}

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

    }
}