using System;

namespace Celeste.Mod.CelesteNet.Server {
    public static class ConnFeatureUtils {

        public const char kvSeparator = '#';
        public const string CHECK_ENTRY_ENV = "CheckEnv";
        public const string CHECK_ENTRY_MAC = "CheckMAC";
        public const string CHECK_ENTRY_DEV = "CheckDevice";
        public const string CHECK_ENTRY_BAN = "SelfReportBan";

        public static string? ClientCheck(ConPlusTCPUDPConnection Con) {
            string? selfReport;
            string? CheckDevice, CheckDeviceKV;
            string? CheckMAC, CheckMacKV;
            string? CheckEnv, CheckEnvKV;

            if (Con.ConnFeatureData.TryGetValue(CHECK_ENTRY_BAN, out selfReport) && !selfReport.Trim().IsNullOrEmpty()) {
                BanInfo banMe = new() {
                    UID = Con.UID,
                    // I had this prefix it with "Auto-ban" but since we don't have separate "reasons" for internal
                    // documentation vs. what is shown to the client, I'd like to hide the fact that this is an extra
                    // "automated" ban happening.
                    Reason = "-> " + selfReport,
                    From = DateTime.UtcNow
                };
                Con.Server.UserData.Save(Con.UID, banMe);

                Logger.Log(LogLevel.VVV, "frontend", $"Auto-ban of secondary IP: {selfReport} ({Con.UID})");

                return Con.Server.Settings.MessageClientCheckFailed;
            }

            if (!Con.ConnFeatureData.TryGetValue(CHECK_ENTRY_DEV, out CheckDevice) || CheckDevice.Trim().IsNullOrEmpty())
                return Con.Server.Settings.MessageClientCheckFailed;
            CheckDeviceKV = CHECK_ENTRY_DEV + kvSeparator + CheckDevice;

            if (!Con.ConnFeatureData.TryGetValue(CHECK_ENTRY_MAC, out CheckMAC) || CheckMAC.Trim().IsNullOrEmpty())
                return Con.Server.Settings.MessageClientCheckFailed;
            CheckMacKV = CHECK_ENTRY_MAC + kvSeparator + CheckMAC;

            if (!Con.ConnFeatureData.TryGetValue(CHECK_ENTRY_ENV, out CheckEnv) || CheckEnv.Trim().IsNullOrEmpty())
                return Con.Server.Settings.MessageClientCheckFailed;
            CheckEnvKV = CHECK_ENTRY_ENV + kvSeparator + CheckEnv;

            // Check if the player's banned
            BanInfo ban;

            // relying on short-circuiting of || here...
            bool found = Con.Server.UserData.TryLoad(CheckMacKV, out ban)
                || Con.Server.UserData.TryLoad(CheckDeviceKV, out ban)
                || Con.Server.UserData.TryLoad(CheckEnvKV, out ban);

            if (found && (ban.From == null || ban.From <= DateTime.Now) && (ban.To == null || DateTime.Now <= ban.To)) {
                return string.Format(Con.Server.Settings.MessageBan, "", "", ban.Reason);
            }

            return null;
        }
    }
}