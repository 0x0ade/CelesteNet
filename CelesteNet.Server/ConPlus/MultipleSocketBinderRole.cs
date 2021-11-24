using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public abstract class MultipleSocketBinderRole : NetPlusThreadRole {

        public new abstract class RoleWorker : NetPlusThreadRole.RoleWorker {

            protected RoleWorker(NetPlusThreadRole role, NetPlusThread thread) : base(role, thread) {}

            protected internal override void StartWorker(CancellationToken token) {
                using (Socket sock = Role.CreateSocket())
                    StartWorker(sock, token);
            }

            protected abstract void StartWorker(Socket socket, CancellationToken token);

            public new MultipleSocketBinderRole Role => (MultipleSocketBinderRole) base.Role;

        }

        protected MultipleSocketBinderRole(NetPlusThreadPool pool, ProtocolType protocol, EndPoint endPoint) : base(pool) {
            Protocol = protocol;
            EndPoint = endPoint;
        }

        private Socket CreateSocket() {
            Socket? socket = null;
            try {
                // Create socket and perform socket options magic
                socket = new Socket(EndPoint.AddressFamily, Protocol switch {
                    ProtocolType.Tcp => SocketType.Stream,
                    ProtocolType.Udp => SocketType.Dgram,
                    _ => throw new InvalidOperationException($"Unknown protocol type {Protocol}")
                }, Protocol);
                socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
                if (MaxThreads > 1)
                    socket.EnableEndpointReuse();

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