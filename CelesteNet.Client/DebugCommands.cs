using Monocle;

namespace Celeste.Mod.CelesteNet.Client {
    public static class DebugCommands {

        [Command("con", "connect to a celestenet server")]
        public static void Con(string server) {
            CelesteNetClientModule.Settings.Connected = false;
            if (!string.IsNullOrWhiteSpace(server)) {
                CelesteNetClientModule.Settings.HostOverride = server;
            }
            CelesteNetClientModule.Settings.Connected = true;
        }

        [Command("dc", "disconnect from celestenet")]
        public static void DC() {
            CelesteNetClientModule.Settings.Connected = false;
        }

    }
}
