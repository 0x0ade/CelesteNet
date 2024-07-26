using System.Linq;
using Celeste.Mod.CelesteNet.Client.Components;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client {
    public static class DebugCommands {

        [Command("con", "connect to a celestenet server")]
        public static void Con(string server) {
            CelesteNetClientModule.Settings.Connected = false;
            if (!string.IsNullOrWhiteSpace(server)) {
                CelesteNetClientModule.Settings.ServerOverride = server;
            }
            CelesteNetClientModule.Settings.Connected = true;
        }

        [Command("dc", "disconnect from celestenet")]
        public static void DC() {
            CelesteNetClientModule.Settings.Connected = false;
        }

        [Command("picoghosts", "Lists all active ghosts when in PICO-8.")]
        public static void PicoGhosts() {
            var pico8 =
                CelesteNetClientModule.Instance
                    .Context
                    .Get<CelesteNetPico8Component>();
            Engine.Commands.Log($"Ghosts: {string.Join(", ", pico8.ghosts.Values.Select(i => i.ToString()))}");
        }

    }
}
