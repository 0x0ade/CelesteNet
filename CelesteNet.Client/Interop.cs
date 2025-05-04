using MonoMod.ModInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client
{
    [ModExportName("CelesteNet.Client")]
    public static class Interop
    {
        public static bool GetInteractions()
            => CelesteNetClientModule.Session?.InteractionsOverride ?? CelesteNetClientModule.Settings?.InGame?.Interactions ?? false;

        public static bool? GetInteractionsSessionOverride()
            => CelesteNetClientModule.Session?.InteractionsOverride;

        public static void SetInteractionsSessionOverride(bool? value) {
            if (CelesteNetClientModule.Session != null)
                CelesteNetClientModule.Session.InteractionsOverride = value;
        }
    }
}
