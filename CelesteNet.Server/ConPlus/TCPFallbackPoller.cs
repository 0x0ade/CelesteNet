using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    /*
    This poller is slower, but it works on all platforms. Using the EPoll poller
    on Linux is recommended for optimal performance.
    -Popax21
    */
    public class TCPFallbackPoller : TCPReceiverRole.IPoller {

        private readonly byte[] PollBuffer = new byte[0];

        private RWLock PollerLock;
        private HashSet<ConPlusTCPUDPConnection> Cons;
        private BlockingCollection<ConPlusTCPUDPConnection> ConQueue;

        public TCPFallbackPoller() {
            PollerLock = new RWLock();
            Cons = new HashSet<ConPlusTCPUDPConnection>();
            ConQueue = new BlockingCollection<ConPlusTCPUDPConnection>();
        }

        public void Dispose() {
            using (PollerLock.W()) {
                Cons.Clear();
                ConQueue.Dispose();
                PollerLock.Dispose();
            }
        }

        public void AddConnection(ConPlusTCPUDPConnection con) {
            using (PollerLock.W())
                Cons.Add(con);
            ArmConnectionPoll(con);
        }

        public void RemoveConnection(ConPlusTCPUDPConnection con) {
            using (PollerLock.W())
                Cons.Remove(con);
        }

        public IEnumerable<ConPlusTCPUDPConnection> StartPolling(TCPReceiverRole role, CancellationToken token) {
            return ConQueue.GetConsumingEnumerable(token);
        }

        public void ArmConnectionPoll(ConPlusTCPUDPConnection con) {
            using (PollerLock.R()) {
                if (!Cons.Contains(con))
                    return;

                con.TCPSocket.BeginReceive(PollBuffer, 0, 0, SocketFlags.None, _ => {
                    using (PollerLock.R())
                        if (Cons.Contains(con))
                            ConQueue.Add(con);
                }, null);
            }
        }

    }
}