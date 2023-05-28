using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    [Serializable]
    public struct CelestePlayerInventory {
        public static readonly CelestePlayerInventory Prologue = new CelestePlayerInventory(0, dreamDash: false);

        public static readonly CelestePlayerInventory Default = new CelestePlayerInventory(1, dreamDash: true, backpack: true, noRefills: false);

        public static readonly CelestePlayerInventory OldSite = new CelestePlayerInventory(1, dreamDash: false);

        public static readonly CelestePlayerInventory CH6End = new CelestePlayerInventory(2);

        public static readonly CelestePlayerInventory TheSummit = new CelestePlayerInventory(2, dreamDash: true, backpack: false);

        public static readonly CelestePlayerInventory Core = new CelestePlayerInventory(2, dreamDash: true, backpack: true, noRefills: true);

        public static readonly CelestePlayerInventory Farewell = new CelestePlayerInventory(1, dreamDash: true, backpack: false);

        public int Dashes;

        public bool DreamDash;

        public bool Backpack;

        public bool NoRefills;

        public CelestePlayerInventory(int dashes = 1, bool dreamDash = true, bool backpack = true, bool noRefills = false) {
            Dashes = dashes;
            DreamDash = dreamDash;
            Backpack = backpack;
            NoRefills = noRefills;
        }
    }
}
