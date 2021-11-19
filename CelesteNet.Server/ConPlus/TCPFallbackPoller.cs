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

        private RWLock pollerLock;
        private HashSet<ConPlusTCPUDPConnection> cons;
        private BlockingCollection<ConPlusTCPUDPConnection> conQueue;

        public TCPFallbackPoller() {
            pollerLock = new RWLock();
            cons = new HashSet<ConPlusTCPUDPConnection>();
            conQueue = new BlockingCollection<ConPlusTCPUDPConnection>();
        }

        public void Dispose() {
            using (pollerLock.W()) {
                cons.Clear();
                conQueue.Dispose();
                pollerLock.Dispose();
            }
        }

        public void AddConnection(ConPlusTCPUDPConnection con) {
            using (pollerLock.W())
                cons.Add(con);
            ArmConnectionPoll(con);
        }

        public void RemoveConnection(ConPlusTCPUDPConnection con) {
            using (pollerLock.W())
                cons.Remove(con);
        }

        public IEnumerable<ConPlusTCPUDPConnection> StartPolling(TCPReceiverRole role, CancellationToken token) {
            return conQueue.GetConsumingEnumerable(token);
        }

        public void ArmConnectionPoll(ConPlusTCPUDPConnection con) {
            using (pollerLock.R()) {
                con.TCPSocket.BeginReceive(null!, 0, 0, SocketFlags.None, _ => {
                    using (pollerLock.R())
                        if (cons.Contains(con))
                            conQueue.Add(con);
                }, null!);
            }
        }

    }
}