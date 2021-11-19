using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System;

namespace Celeste.Mod.CelesteNet.Server {
    /*
    EPoll can be used on Linux to achieve a similar load-balancing effect for
    TCP polling as with port reusing. This means that we can get very good
    performance from utilizing it. It was already a topic of passionate debate
    if this should be included, but I decided to include it as the performance
    gains are to siginficant to ignore.
    Used sources:
        epoll: https://linux.die.net/man/7/epoll
        eventfd: https://linux.die.net/man/2/eventfd
    -Popax21
    */
    public class TCPEPollPoller : TCPReceiverRole.IPoller {

        // This structure marshals as struct epoll_event
        [StructLayout(LayoutKind.Sequential)]
        private struct epoll_event {

            public UInt32 events;
            public long user;

        }

        [DllImport("libc", SetLastError = true)]
        private static extern int epoll_create(int size);
        [DllImport("libc", SetLastError = true)]
        private static extern int eventfd(uint initval, int flags);
        [DllImport("libc", SetLastError = true)]
        private static extern nint read(int fd, [In, MarshalAs(UnmanagedType.LPArray)] byte[] buf, nint size);
        [DllImport("libc", SetLastError = true)]
        private static extern nint write(int fd, [In, MarshalAs(UnmanagedType.LPArray)] byte[] buf, nint size);
        [DllImport("libc", SetLastError = true)]
        private static extern void close(int fd);

        private const int EBADF = 9;
        private const int EPOLL_CTL_ADD = 1, EPOLL_CTL_DEL = 2, EPOLL_CTL_MOD = 3;
        private const uint EPOLLIN = 0x00000001u, EPOLLRDHUP = 0x00002000u, EPOLLERR = 0x00000008u, EPOLLET = 0x80000000u, EPOLLONESHOT = 0x40000000u;
        [DllImport("libc", SetLastError = true)]
        private static extern int epoll_ctl(int epfd, uint op, int fd, [In] in epoll_event evt);
        
        [DllImport("libc", SetLastError = true)]
        private static extern int epoll_wait(int epfd, [Out, MarshalAs(UnmanagedType.LPArray)] epoll_event[] evt, int maxevts, int timeout);
        
        private RWLock pollerLock;
        private int epollFD, cancelFD;
        private ConcurrentDictionary<ConPlusTCPUDPConnection, int> connections;

        private int nextConId;
        private ConcurrentDictionary<int, ConPlusTCPUDPConnection> conIds;

        public TCPEPollPoller() {
            pollerLock = new RWLock();
            connections = new ConcurrentDictionary<ConPlusTCPUDPConnection, int>();
            conIds = new ConcurrentDictionary<int, ConPlusTCPUDPConnection>();

            // Create the EPoll FD
            epollFD = epoll_create(21); // The documentation says "pass any number, it's unused on modern systems" :)
            if (epollFD < 0)
                throw new SystemException($"Could not create the EPoll FD: {Marshal.GetLastWin32Error()}");
                
            // Create the cancel eventfd
            cancelFD = eventfd(0, 0);
            if (cancelFD < 0)
                throw new SystemException($"Could not create the cancel eventfd: {Marshal.GetLastWin32Error()}");

            // Make the EPoll FD also listen for the cancel eventfd
            epoll_event evt = new epoll_event() {
                events = EPOLLIN,
                user = int.MaxValue
            };
            if (epoll_ctl(epollFD, EPOLL_CTL_ADD, cancelFD, in evt) < 0)
                throw new SystemException($"Could not add the cancel eventfd to the EPoll FD: {Marshal.GetLastWin32Error()}");

        }

        public void Dispose() {
            lock (pollerLock.W()) {
                // Close the EPoll and cancel pipe FDs
                close(epollFD);
                close(cancelFD);

                connections.Clear();
                conIds.Clear();
                pollerLock.Dispose();
            }
        }

        public void AddConnection(ConPlusTCPUDPConnection con) {
            using (pollerLock.W()) {
                // Assign the connection an ID
                int id = int.MaxValue;
                while (id == int.MaxValue)
                    id = Interlocked.Increment(ref nextConId);
                if (!connections.TryAdd(con, id))
                    throw new ArgumentException("Connection already part of poller");
                conIds[id] = con;
                
                // Add the socket's FD to the EPoll FD
                // Flag breakdown:
                //  - EPOLLIN: listen for "read available"
                //  - EPOLLRDHUP: listen for "remote closed the connection"
                //  - EPOLLERR: listen for "an error occured"
                //  - EPOLLET: enable "edge triggering", which causes two thing:
                //    - the poll call only returns a socket when it's state
                //      changed, not when we forgot to read all bytes
                //    - the Linux kernel will only wake up one thread polling on
                //      the EPoll FD for a certain event (the behaviour we want)
                //  - EPOLLONESHOT: disable the socket for further polling until
                //      we enable it again. This ensures that the same
                //      connection isn't handled by two threads at the same time
                epoll_event evt = new epoll_event() {
                    events = EPOLLIN | EPOLLRDHUP | EPOLLERR | EPOLLET | EPOLLONESHOT,
                    user = id
                };
                if (epoll_ctl(epollFD, EPOLL_CTL_ADD, (int) con.TCPSocket.Handle, in evt) < 0)
                    throw new SystemException($"Couldn't add connection socket to EPoll FD: {Marshal.GetLastWin32Error()}");
            }
        }

        public void RemoveConnection(ConPlusTCPUDPConnection con) {
            using (pollerLock.W()) {
                // Remove the connection from the ID table
                if (!connections.TryRemove(con, out int id))
                    throw new ArgumentException("Connection not part of poller");
                conIds.TryRemove(id, out _);

                // Remove the socket from the EPoll FD
                // Because of a bug in old Linux versions we still have to pass something as evt
                // Maybe the socket was already closed, in which case it already
                // got removed from the EPoll FD and it will return EBADF
                epoll_event evt = default;
                int ret = epoll_ctl(epollFD, EPOLL_CTL_DEL, (int) con.TCPSocket.Handle, in evt);
                if (ret < 0 && Marshal.GetLastWin32Error() != EBADF)
                    throw new SystemException($"Couldn't remove connection socket from EPoll FD: {Marshal.GetLastWin32Error()}");
            }
        }

        public IEnumerable<ConPlusTCPUDPConnection> StartPolling(TCPReceiverRole role, CancellationToken token) {
            // We somehow have to be able to cancel the epoll_wait call. The
            // cleanest way I found to do this is to create an eventfd
            // (basically a low level ManualResetEvent), and increment it
            // whenever a token is triggered, and reset it when threads return
            // from polling. This causes all threads to cycle out of their poll
            // loops and check their tokens to determine if they should exit.
            epoll_event[] evts = new epoll_event[role.Server.Settings.TCPPollMaxEvents];
            byte[] clearBuf = new byte[8];
            using (token.Register(() => write(cancelFD, BitConverter.GetBytes((UInt64) 1), 8)))
            while (!token.IsCancellationRequested) {
                // Poll the EPoll FD
                int ret = epoll_wait(epollFD, evts, 1, -1);
                if (ret < 0)
                    throw new SystemException($"Couldn't poll the EPoll FD: {Marshal.GetLastWin32Error()}");
                
                // Yield the connections from the event
                for (int i = 0; i < ret; i++) {
                    int id = (int) evts[i].user;
                    if (id == int.MaxValue) {
                        // Someone incremented the cancel eventfd
                        // Just clear it
                        read(cancelFD, clearBuf, 8);
                        continue;
                    }
                    yield return conIds[id];
                }
            }
        }

        public void ArmConnectionPoll(ConPlusTCPUDPConnection con) {
            using (pollerLock.R()) {
                if (!connections.TryGetValue(con, out int id))
                    throw new ArgumentException("Connection not part of poller");

                // Modify all flags to how they were originally
                // Maybe the socket was already closed, in which case it already
                // got removed from the EPoll FD and it will return EBADF
                epoll_event evt = new epoll_event() {
                    events = EPOLLIN | EPOLLRDHUP | EPOLLERR | EPOLLET | EPOLLONESHOT,
                    user = id
                };
                int ret = epoll_ctl(epollFD, EPOLL_CTL_MOD, (int) con.TCPSocket.Handle, in evt);
                if (ret < 0 && Marshal.GetLastWin32Error() != EBADF)
                    throw new SystemException($"Couldn't arm connection socket for EPoll FD: {Marshal.GetLastWin32Error()}");
            }
        }

    }
}