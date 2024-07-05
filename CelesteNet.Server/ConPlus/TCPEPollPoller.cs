using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    /*
    EPoll can be used on Linux to achieve a similar load-balancing effect for
    TCP polling as with port reusing. This means that we can get very good
    performance from utilizing it. It was already a topic of passionate debate
    if this should be included, but I decided to include it as the performance
    gains are too siginficant to ignore.
    Used sources:
        epoll: https://linux.die.net/man/7/epoll
        eventfd: https://linux.die.net/man/2/eventfd
    -Popax21
    */
    public class TCPEPollPoller : TCPReceiverRole.IPoller {

        // This structure marshals as struct epoll_event
        [StructLayout(LayoutKind.Sequential)]
        private struct epoll_event {

            public uint events;
            public long user;

        }

        private const int EINTR = 4, EBADF = 9;

        private const int EFD_SEMAPHORE = 1;
        [DllImport("libc", SetLastError = true)]
        private static extern int eventfd(uint initval, int flags);
        [DllImport("libc", SetLastError = true)]
        private static extern nint read(int fd, [In, MarshalAs(UnmanagedType.LPArray)] byte[] buf, nint size);
        [DllImport("libc", SetLastError = true)]
        private static extern nint write(int fd, [In, MarshalAs(UnmanagedType.LPArray)] byte[] buf, nint size);
        [DllImport("libc", SetLastError = true)]
        private static extern void close(int fd);

        private const int EPOLL_CTL_ADD = 1, EPOLL_CTL_DEL = 2, EPOLL_CTL_MOD = 3;
        private const uint EPOLLIN = 0x00000001u, EPOLLERR = 0x00000008u, EPOLLHUP = 0x00000010, EPOLLRDHUP = 0x00002000u, EPOLLET = 0x80000000u, EPOLLONESHOT = 0x40000000u;
        [DllImport("libc", SetLastError = true)]
        private static extern int epoll_create(int size);
        [DllImport("libc", SetLastError = true)]
        private static extern int epoll_ctl(int epfd, uint op, int fd, [In] in epoll_event evt);

        [DllImport("libc", SetLastError = true)]
        private static extern int epoll_wait(int epfd, [Out, MarshalAs(UnmanagedType.LPArray)] epoll_event[] evt, int maxevts, int timeout);

        private readonly RWLock PollerLock = new();
        private readonly int EpollFD, CancelFD;
        private readonly ConcurrentDictionary<ConPlusTCPUDPConnection, (int fd, int id)> Connections = new();

        private int NextConId;
        private readonly ConcurrentDictionary<int, ConPlusTCPUDPConnection> ConIds = new();

        public TCPEPollPoller() {
            // Create the EPoll FD
            EpollFD = epoll_create(21); // The documentation says "pass any number, it's unused on modern systems" :)
            if (EpollFD < 0)
                throw new SystemException($"Could not create the EPoll FD: {Marshal.GetLastWin32Error()}");

            // Create the cancel eventfd, and initialize it to be not triggered
            // (counter at zero) and behave like a semaphore (so reading it
            // causes to counter to get decremented, not reset to zero)
            CancelFD = eventfd(0, EFD_SEMAPHORE);
            if (CancelFD < 0)
                throw new SystemException($"Could not create the cancel eventfd: {Marshal.GetLastWin32Error()}");

            // Make the EPoll FD also listen for the cancel eventfd
            // A eventfd is read-ready when it's counter is above zero
            // We don't specifiy EPOLLET (edge triggered) and EPOLLONESHOT
            // (don't poll the FD after it's triggered once until renabled), as
            // we want to wake up all threads
            epoll_event evt = new() {
                events = EPOLLIN,
                user = int.MaxValue
            };
            if (epoll_ctl(EpollFD, EPOLL_CTL_ADD, CancelFD, in evt) < 0)
                throw new SystemException($"Could not add the cancel eventfd to the EPoll FD: {Marshal.GetLastWin32Error()}");

        }

        public void Dispose() {
            lock (PollerLock.W()) {
                // Close the EPoll and cancel eventfd
                close(EpollFD);
                close(CancelFD);

                Connections.Clear();
                ConIds.Clear();
                PollerLock.Dispose();
            }
        }

        public void AddConnection(ConPlusTCPUDPConnection con) {
            using (PollerLock.W()) {
                int fd = (int) con.TCPSocket.Handle;

                // Assign the connection an ID
                // Don't assing int.MaxValue, as it is used by the cancel eventfd
                int id = int.MaxValue;
                while (id == int.MaxValue)
                    id = Interlocked.Increment(ref NextConId);
                if (!Connections.TryAdd(con, (fd, id)))
                    throw new ArgumentException("Connection already part of poller");
                ConIds[id] = con;

                // Add the socket's FD to the EPoll FD
                // Flag breakdown:
                //  - EPOLLIN: listen for "read-ready"
                //  - EPOLLERR: listen for "an error occured"
                //  - EPOLLHUP: listen for "the connection was closed"
                //  - EPOLLRDHUP: listen for "remote closed the connection"
                //      (I don't know why there are two similar events for this)
                //  - EPOLLET: enable "edge triggering", which causes two thing:
                //    - the poll call only returns a socket when it's state
                //      changed, not when we forgot to clear the trigger
                //      condition (like not reading all bytes available)
                //    - the Linux kernel will only wake up one thread polling on
                //      the EPoll FD for a certain event (the load-balancing
                //      behaviour we want)
                //  - EPOLLONESHOT: disable the socket for further polling until
                //      we enable it again. This ensures that the same
                //      connection isn't handled by two threads at the same time
                //      when the connection receives additional data when a
                //      thread's already handling it
                epoll_event evt = new() {
                    events = EPOLLIN | EPOLLERR | EPOLLHUP | EPOLLRDHUP | EPOLLET | EPOLLONESHOT,
                    user = id
                };
                if (epoll_ctl(EpollFD, EPOLL_CTL_ADD, fd, in evt) < 0)
                    throw new SystemException($"Couldn't add connection socket to EPoll FD: {Marshal.GetLastWin32Error()}");
            }
        }

        public void RemoveConnection(ConPlusTCPUDPConnection con) {
            using (PollerLock.W()) {
                // Remove the connection from the ID table
                if (!Connections.TryRemove(con, out (int fd, int id) conData))
                    return;
                ConIds.TryRemove(conData.id, out _);

                // Remove the socket from the EPoll FD
                // Because of a bug in old Linux versions we still have to pass
                // something as evt, even though it isn't used
                // Maybe the socket was already closed, in which case it already
                // got removed from the EPoll FD and epoll_ctl will return EBADF
                epoll_event evt = default;
                int ret = epoll_ctl(EpollFD, EPOLL_CTL_DEL, conData.fd, in evt);
                if (ret < 0 && Marshal.GetLastWin32Error() != EBADF)
                    throw new SystemException($"Couldn't remove connection socket from EPoll FD: {Marshal.GetLastWin32Error()}");
            }
        }

        public IEnumerable<ConPlusTCPUDPConnection> StartPolling(TCPReceiverRole role, CancellationToken token) {
            // We somehow have to be able to cancel the epoll_wait call. The
            // cleanest way I found to do this is to create an eventfd
            // (basically a low level ManualResetEvent), and increment it
            // whenever a token is triggered, and decrement it when a thread exits
            // Triggering it causes all threads to cycle out of their poll
            // loops and check their tokens to determine if they should exit.
            epoll_event[] evts = new epoll_event[role.Server.Settings.TCPPollMaxEvents];
            using (token.Register(() => write(CancelFD, BitConverter.GetBytes(1UL), 8)))
                while (!token.IsCancellationRequested) {
                    // Poll the EPoll FD
                    int ret;
                    do {
                        ret = epoll_wait(EpollFD, evts, 1, -1);
                        // leave both loops when token gets cancelled, jumping straight to end
                        if (token.IsCancellationRequested)
                            goto Cancelled;
                    } while (ret == EINTR);

                    if (ret < 0)
                        throw new SystemException($"Couldn't poll the EPoll FD: {Marshal.GetLastWin32Error()}");

                    // Yield the Connections from the event
                    for (int i = 0; i < ret; i++) {
                        int id = (int) evts[i].user;
                        if (id != int.MaxValue)
                            if (ConIds.TryGetValue(id, out ConPlusTCPUDPConnection? conId))
                                yield return conId!;
                            else
                                Logger.Log(LogLevel.WRN, "epoll", $"No ConPlusTCPUDPConnection found for id {id}, skipping event");
                    }
                }

            Cancelled:
            // The eventfd got incremented for us to exit, so decrement it
            read(CancelFD, new byte[8], 8);
        }

        public void ArmConnectionPoll(ConPlusTCPUDPConnection con) {
            using (PollerLock.R()) {
                if (!Connections.TryGetValue(con, out (int fd, int id) conData))
                    return;

                // Modify all flags to how they were originally. This causes the
                // EPoll FD to monitor the socket again
                // Maybe the socket was already closed, in which case it already
                // got removed from the EPoll FD and epoll_ctl will return EBADF
                epoll_event evt = new() {
                    events = EPOLLIN | EPOLLERR | EPOLLHUP | EPOLLRDHUP | EPOLLET | EPOLLONESHOT,
                    user = conData.id
                };
                int ret = epoll_ctl(EpollFD, EPOLL_CTL_MOD, conData.fd, in evt);
                if (ret < 0 && Marshal.GetLastWin32Error() != EBADF)
                    throw new SystemException($"Couldn't arm connection socket for EPoll FD: {Marshal.GetLastWin32Error()}");
            }
        }

    }
}