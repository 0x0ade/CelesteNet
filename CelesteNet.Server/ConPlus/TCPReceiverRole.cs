using Celeste.Mod.CelesteNet.Server;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet {
    public class TCPReceiverRole : NetPlusThreadRole {

        public interface IPoller : IDisposable {

            void AddConnection(ConPlusTCPUDPConnection con);
            void RemoveConnection(ConPlusTCPUDPConnection con);
            IEnumerable<ConPlusTCPUDPConnection> StartPolling(TCPReceiverRole role, CancellationToken token);
            void ArmConnectionPoll(ConPlusTCPUDPConnection con);

        }

        private class Worker : RoleWorker {

            public Worker(TCPReceiverRole role, NetPlusThread thread) : base(role, thread) {}

            protected internal override void StartWorker(CancellationToken token) {
                // Poll for connections
                foreach (ConPlusTCPUDPConnection con in Role.Poller.StartPolling(Role, token)) {
                    EnterActiveZone();
                    try {
                        // Receive data from the connection
                        con.HandleTCPData();

                        // Arm the connection to be polled again
                        if (con.IsConnected)
                            Role.Poller.ArmConnectionPoll(con);
                    } catch (Exception e) {
                        if (e is SocketException se && se.IsDisconnect()) {
                            Logger.Log(LogLevel.INF, "tcprecv", $"Remote of connection {con} closed the connection");
                            con.DisposeSafe();
                            continue;
                        }

                        Logger.Log(LogLevel.WRN, "tcprecv", $"Error while reading from connection {con}: {e}");
                        con.DisposeSafe();
                    } finally {
                        ExitActiveZone();
                    }
                }
            }

            public new TCPReceiverRole Role => (TCPReceiverRole) base.Role;

        }

        public TCPReceiverRole(NetPlusThreadPool pool, CelesteNetServer server, IPoller poller) : base(pool) {
            Server = server;
            Poller = poller;
        }

        public override void Dispose() {
            Poller.Dispose();
            base.Dispose();
        }

        public override RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

        public override int MinThreads => 1;
        public override int MaxThreads => int.MaxValue;

        public CelesteNetServer Server { get; }
        public IPoller Poller { get; }

    }
}