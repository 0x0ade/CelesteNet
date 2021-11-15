using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public abstract class MultipleSocketBinderRole : NetPlusThreadRole {

        private const int SOL_SOCKET = 1, SO_REUSEPORT = 15;
        [DllImport("libc", SetLastError = true)] 
        private static extern int setsockopt(IntPtr socket, int level, int opt, [In, MarshalAs(UnmanagedType.LPArray)] int[] val, int len);

        public new abstract class RoleWorker : NetPlusThreadRole.RoleWorker {

            protected RoleWorker(NetPlusThreadRole role, NetPlusThread thread) : base(role, thread) {}
            
            protected internal override void StartWorker(CancellationToken token) {
                using (Socket sock = Role.CreateSocket())
                    StartWorker(sock, token);
            }

            protected abstract void StartWorker(Socket socket, CancellationToken token);

            public new MultipleSocketBinderRole Role => (MultipleSocketBinderRole) base.Role;

        }

        private int firstSocket = 0;

        protected MultipleSocketBinderRole(NetPlusThreadPool pool, ProtocolType protocol, EndPoint endPoint) : base(pool) {
            Protocol = protocol;
            EndPoint = endPoint;
        }

        private Socket CreateSocket() {
            Socket? socket = null;
            try {
                // Create socket and perform socket options magic
                socket = new Socket(Protocol switch {
                    ProtocolType.Tcp => SocketType.Stream,
                    ProtocolType.Udp => SocketType.Dgram,
                    _ => throw new InvalidOperationException($"Unknown protocol type {Protocol}")
                }, Protocol);
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);

                if (MaxThreads > 1) {
                    // Set reuse address and port options (if available)
                    // We have to set SO_REUSEPORT directly though
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
                    socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.ReuseAddress, true);
                    if (Environment.OSVersion.Platform == PlatformID.Unix) {
                        if (setsockopt(socket.Handle, SOL_SOCKET, SO_REUSEPORT, new[] { 1 }, sizeof(int)) < 0) {
                            // Even though the method is named GetLastWin32Error, it still works on other platforms
                            // NET 6.0 added the better named method GetLastPInvokeError, which does the same thing
                            // However, still use GetLastWin32Error to remain compatible with the net452 build target
                            Logger.Log(LogLevel.WRN, "conplus", $"Failed enabling socket option SO_REUSEPORT for socket belonging to role {this}: {Marshal.GetLastWin32Error()}");
                        }
                    } else if (Interlocked.Exchange(ref firstSocket, 1) <= 0) { // Interlocked doesn't support bools
                        // We only have an advantage with multiple threads when SO_REUSEPORT is supported
                        // It tells the Linux kernel to distribute incoming packets evenly among all sockets bound to that port
                        // However, Windows doesn't support it, and it's SO_REUSEADDR behaviour is that only one socket will receive everything 
                        // As such only one thread will handle all messages and actually do any work
                        Logger.Log(LogLevel.WRN, "tcpudp", "Starting more than one UDP thread on a platform without SO_REUSEPORT!");
                    }
                }

                // Bind the socket
                socket.Bind(EndPoint);

                return socket;
            } catch (Exception) {
                if (socket != null)
                    socket.Dispose();
                throw;
            }
        }

        public ProtocolType Protocol { get; }
        public EndPoint EndPoint { get; }

        public override int MinThreads => 1;
        public override int MaxThreads => (Environment.OSVersion.Platform != PlatformID.Unix) ? 1 : int.MaxValue;
    }
}