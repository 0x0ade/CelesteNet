using System;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Celeste.Mod.CelesteNet.Server {
    public static class ServerUtils {

        private const int SOL_SOCKET = 1, SO_REUSEPORT = 15;
        [DllImport("libc", SetLastError = true)]
        private static extern int setsockopt(IntPtr socket, int level, int opt, [In, MarshalAs(UnmanagedType.LPArray)] int[] val, int len);

        public static void EnableEndpointReuse(this Socket sock) {
            // Set reuse address and port options (if available)
            // We have to set SO_REUSEPORT directly though
            sock.ExclusiveAddressUse = false;
            switch (sock.AddressFamily) {
                case AddressFamily.InterNetwork:
                    sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
                    break;
                case AddressFamily.InterNetworkV6:
                    sock.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.ReuseAddress, true);
                    break;
            }

            //All Unix-ish systems should have SO_REUSEPORT
            if (MonoMod.Utils.PlatformHelper.Is(MonoMod.Utils.Platform.Unix)) {
                if (setsockopt(sock.Handle, SOL_SOCKET, SO_REUSEPORT, new[] { 1 }, sizeof(int)) < 0)
                    throw new SystemException($"Could not set SO_REUSEPORT: {Marshal.GetLastWin32Error()}");
            }
        }

    }
}