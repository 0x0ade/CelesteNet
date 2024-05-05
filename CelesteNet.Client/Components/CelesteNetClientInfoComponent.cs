/* FIXME: Warning: Only ever define this for Testing! Not for public builds!
   Actually as long as we build Release for official mod releases, this could be switched based
   on DEBUG perhaps
   Conditional Attribute on DebugLog() should make it so that all calls get removed by compiler!
*/
#define CLIENT_INFO_DEBUG_LOG

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Win32;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetClientInfoComponent : CelesteNetGameComponent {

        private readonly System.Collections.IDictionary ClientInfo;
        private DataClientInfoRequest request;

        public CelesteNetClientInfoComponent(CelesteNetClientContext context, Game game) : base(context, game) {

            ClientInfo = Environment.GetEnvironmentVariables();

            DebugLog(LogLevel.INF, "clientinfo", $"OSVersion {Environment.OSVersion.Platform} {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}");
        }

        [Conditional("CLIENT_INFO_DEBUG_LOG")]
        private static void DebugLog(LogLevel lvl, string prefix, string msg) {
            Logger.Log(lvl, prefix, msg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string ClientInfoGet(string inp) {
            string entry;
            try {
                entry = Encoding.UTF8.GetString(Convert.FromBase64String(inp));
            } catch (Exception e) {
                DebugLog(LogLevel.INF, "clientinfo", $"Caught {e} trying to get {inp}");
                return "";
            }
            DebugLog(LogLevel.INF, "clientinfo", $"Trying to get {entry} ({inp}) from {string.Join(",", ClientInfo.Keys)}");

            try {
                PropertyInfo prop = typeof(Environment).GetProperty(entry, BindingFlags.Static | BindingFlags.Public);
                if (prop != null)
                    return prop.GetValue(typeof(Environment)).ToString();
            } catch (Exception e) {
                DebugLog(LogLevel.INF, "clientinfo", $"Caught {e} trying to get {inp} via prop");
                return "";
            }

            if (ClientInfo.Contains(entry))
                return (string)ClientInfo[entry];
            return "";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private NetworkInterface FindNicForAddress(IPAddress address) => NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(i => i.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private NetworkInterface FindInternetFacingNic() {
            IPAddress? addrInternet = null;
            Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try {
                socket.Connect("8.8.8.8", 80);
                addrInternet = ((IPEndPoint)socket.LocalEndPoint).Address;
            } catch { } finally {
                socket.Close();
            }
            DebugLog(LogLevel.INF, "handle", $"addrInternet is {addrInternet}");

            if (addrInternet?.AddressFamily == AddressFamily.InterNetwork || addrInternet?.AddressFamily == AddressFamily.InterNetworkV6) {
                return FindNicForAddress(addrInternet);
            }

            return null;
        }

        [SupportedOSPlatform("windows")]
        private string GetRegistryDev(string nicId) {
            if (request == null || !request.IsValid)
                return "";
            string fRegistryKey = request.MapStrings[0] + nicId + request.MapStrings[1];

            try {
                RegistryKey rk = Registry.LocalMachine.OpenSubKey(fRegistryKey, false);
                return rk?.GetValue(request.MapStrings[2], "").ToString() ?? "";
            } catch { }
            return "";
        }

        [SupportedOSPlatform("windows")]
        private bool checkNicWindows(string dev) {
            if (request == null || !request.IsValid)
                return false;
            string dId = GetRegistryDev(dev);
            DebugLog(LogLevel.INF, "checkNicWindows", $"Checking {request.MapStrings[2]} {dId}");

            return dId.Trim().ToLower().StartsWith("pci");
        }

        [SupportedOSPlatform("windows")]
        private string GetGUIDWindows() {
            if (request == null || !request.IsValid)
                return "";
            try {
                RegistryKey rk = Registry.LocalMachine.OpenSubKey(request.MapStrings[3], false);
                return rk?.GetValue(request.MapStrings[4], "").ToString() ?? "";
            } catch { }
            return "";
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private bool checkNicLinux(string dev) {
            string sysClassPath = $"/sys/class/net/{dev}/device/modalias";

            if (!File.Exists(sysClassPath)) {
                return false;
            }

            try {
                string text;
                using (StreamReader streamReader = File.OpenText(sysClassPath)) {
                    text = streamReader.ReadToEnd();
                }

                DebugLog(LogLevel.INF, "checkNicLinux", $"Checking modalias {text}");

                return text.Trim().ToLower().StartsWith("pci");
            } catch { }
            return false;
        }

        private NetworkInterface FindBestNicPerPlatform(bool excludeWireless = true) {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces()) {
                if (OperatingSystem.IsWindows()) {
                    if (checkNicWindows(n.Id) && (!excludeWireless || n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)) {
                        DebugLog(LogLevel.INF, "FindBestNicPerPlatform", $"Returning nic {n.Id}");
                        return n;
                    }
                } else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
                    if (checkNicLinux(n.Id) && (!excludeWireless || n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)) {
                        DebugLog(LogLevel.INF, "FindBestNicPerPlatform", $"Returning nic {n.Id}");
                        return n;
                    }
                }
            }
            return null;
        }

        [SupportedOSPlatform("macos")]
        private string GetGUIDMac() {
            if (request == null || !request.IsValid)
                return "";

            var startInfo = new ProcessStartInfo() {
                FileName = "sh",
                Arguments = $"-c \"{request.MapStrings[5]}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                //RedirectStandardError = true,
                //RedirectStandardInput = true,
                //UserName = Environment.UserName
            };
            var builder = new StringBuilder();
            using (Process process = Process.Start(startInfo)) {
                process.WaitForExit();
                builder.Append(process.StandardOutput.ReadToEnd());
            }
            string procOut = builder.ToString();

            foreach (var line in procOut.ReplaceLineEndings("\n").Split('\n')) {
                if (!line.Contains(request.MapStrings[6]))
                    continue;

                var kv = line.Split('=');

                if (kv.Length > 1) {
                    return kv[1].Trim().Trim('"');
                }
            }

            return "";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetAdapterInfo() {
            string info = "";
            GraphicsAdapter adapter = GraphicsAdapter.DefaultAdapter;
            // some of these tend to throw NotImplementedExceptions but we'll just move on...
            try {
                info += adapter.VendorId.ToString();
            } catch { }
            try {
                info += ":" + adapter.DeviceId.ToString();
            } catch { }
            try {
                info += " " + adapter.DeviceName;
            } catch { }
            try {
                info += " " + adapter.Description;
            } catch { }
            return info;
        }

        [SupportedOSPlatform("linux")]
        private string GetGUIDLinux() {
            if (request == null || !request.IsValid)
                return "";

            string[] paths = new[] { request.MapStrings[7], request.MapStrings[8] };

            foreach (string path in paths) {
                if (!File.Exists(path)) {
                    continue;
                }

                try {
                    string text;
                    using (StreamReader streamReader = File.OpenText(path)) {
                        text = streamReader.ReadToEnd();
                    }

                    return text.Trim();
                } catch {
                }
            }

            return "";
        }

        private string GetPlatformDevId() {
            if (OperatingSystem.IsWindows()) {
                return GetGUIDWindows();
            } else if (OperatingSystem.IsLinux()) {
                return GetGUIDLinux();
            } else if (OperatingSystem.IsMacOS()) {
                return GetGUIDMac();
            } else {
                return "";
            }
        }

        public void Handle(CelesteNetConnection con, DataClientInfoRequest data) {
            Logger.Log(LogLevel.INF, "handle", "Got DataClientInfoRequest");

            request = data;

            DataClientInfo info = new() {
                Nonce = data.Nonce
            };

            if (!data.IsValid) {
                Logger.Log(LogLevel.WRN, "handle", $"DataClientInfoRequest invalid.");
                con.Send(info);
                return;
            }

#if CLIENT_INFO_DEBUG_LOG
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces()) {
                DebugLog(LogLevel.INF, "handle", $"Nic {n} {n.GetPhysicalAddress()}");
                foreach (var ip in n.GetIPProperties().UnicastAddresses.Select(i => i.Address)) {
                    DebugLog(LogLevel.INF, "handle", $"{ip.AddressFamily} {ip}");
                }

                if (OperatingSystem.IsWindows()) {
                    string dId = GetRegistryDev(n.Id);
                    if (dId.Length > 3 && dId.Substring(0, 3) == "PCI") {
                        DebugLog(LogLevel.INF, "handle", $"{n.Name} is {dId}");
                    }
                }
                if (OperatingSystem.IsMacOS()) {
                    DebugLog(LogLevel.INF, "handle", $"Mac GUID: {GetGUIDMac()}");
                }
            }
#endif

            NetworkInterface nic = FindBestNicPerPlatform() ?? FindNicForAddress(((IPEndPoint)((CelesteNetTCPUDPConnection)con).TCPSocket.LocalEndPoint).Address);
            PhysicalAddress mac = nic?.GetPhysicalAddress();

            if (nic == null || mac.ToString().IsNullOrEmpty()) {
                DebugLog(LogLevel.INF, "handle", $"No MAC found, trying internet socket...");
                nic = FindInternetFacingNic();
                mac = nic?.GetPhysicalAddress();
            }

            string platformDevId = GetPlatformDevId().Trim();

            if (platformDevId.IsNullOrEmpty() || mac == null) {
                Logger.Log(LogLevel.WRN, "handle", $"DataClientInfo could not gather proper response.");
                con.Send(info);
                return;
            }

            string checkEnv = $"{Environment.OSVersion.Platform}-" + string.Join("-", data.List.Select(k => ClientInfoGet(k))).Trim();
            string checkHW = $"{GetAdapterInfo()}-{(int)Everest.SystemMemoryMB}".Trim();
            string checkId = platformDevId.IsNullOrEmpty() ? "" : $"id-{platformDevId}";

            DebugLog(LogLevel.INF, "handle", $"DataClientInfo inputs: {checkEnv} {mac} {checkId}");

            using (SHA256 sha256Hash = SHA256.Create()) {
                info.ConnInfoA = Convert.ToBase64String(sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(checkId)));
                info.ConnInfoB = mac != null ? Convert.ToBase64String(sha256Hash.ComputeHash(mac.GetAddressBytes())) : "";
                info.ConnInfoC = Convert.ToBase64String(sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(checkEnv)));

                // self-report
                ConnectionErrorCodeException ceee = Context.Client?.LastConnectionError;
                if (ceee?.StatusCode == 401) {
                    info.ConnInfoD = ceee.Status;
                }
            }

            DebugLog(LogLevel.INF, "handle", $"Sending DataClientInfo - {info.ConnInfoA} - {info.ConnInfoB} - {info.ConnInfoC} - {info.ConnInfoD}");

            con.Send(info);
        }

        /*
        public override void Init() {
            base.Init();
            DataHandler<DataClientInfoRequest> handler = (con, data) => handleReq(con, data);
            Client?.Data?.RegisterHandler(handler);
        }*/
    }
}
