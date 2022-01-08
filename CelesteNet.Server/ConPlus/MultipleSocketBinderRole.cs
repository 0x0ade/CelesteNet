using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public abstract class MultipleSocketBinderRole : NetPlusThreadRole {

        public new abstract class RoleWorker : NetPlusThreadRole.RoleWorker {

            protected RoleWorker(NetPlusThreadRole role, NetPlusThread thread) : base(role, thread) {}

            public new MultipleSocketBinderRole Role => (MultipleSocketBinderRole) base.Role;

            protected internal override void StartWorker(CancellationToken token) {
                using Socket sock = Role.CreateSocket();
                StartWorker(sock, token);
            }

            protected abstract void StartWorker(Socket socket, CancellationToken token);

        }

        public override int MinThreads => 1;
        public override int MaxThreads => MonoMod.Utils.PlatformHelper.Is(MonoMod.Utils.Platform.Linux) ? int.MaxValue : 1;

        public ProtocolType Protocol { get; }
        public EndPoint EndPoint { get; }

        protected MultipleSocketBinderRole(NetPlusThreadPool pool, ProtocolType protocol, EndPoint endPoint) : base(pool) {
            Protocol = protocol;
            EndPoint = endPoint;
        }

        protected virtual Socket CreateSocket() {
            Socket? socket = null;
            try {
                // Create socket and perform socket options magic
                socket = new(EndPoint.AddressFamily, Protocol switch {
                    ProtocolType.Tcp => SocketType.Stream,
                    ProtocolType.Udp => SocketType.Dgram,
                    _ => throw new InvalidOperationException($"Unknown protocol type {Protocol}")
                }, Protocol);
                if (EndPoint.AddressFamily == AddressFamily.InterNetworkV6)
                    socket.DualMode = true;
                if (MaxThreads > 1)
                    socket.EnableEndpointReuse();

                // Bind the socket
                socket.Bind(EndPoint);

                return socket;
            } catch (Exception) {
                socket?.Dispose();
                throw;
            }
        }

    }
}