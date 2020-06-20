using MC = Mono.Cecil;
using CIL = Mono.Cecil.Cil;

using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client {
    public static class DebugCommands {

        [Command("con", "connect to a celestenet server")]
        public static void Con(string server) {
            if (!string.IsNullOrWhiteSpace(server)) {
                CelesteNetClientModule.Settings.Server = server;
            }
            CelesteNetClientModule.Instance.Start();
        }

        [Command("dc", "disconnect from celestenet")]
        public static void DC() {
            CelesteNetClientModule.Instance.Stop();
        }

    }
}
