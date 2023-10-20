using Celeste.Mod.CelesteNet.DataTypes;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Filter {
    public class FilterSettings : CelesteNetServerModuleSettings {

        public bool Enabled { get; set; } = false;
        public bool PrintSessionStatsOnEnd { get; set; } = false;
        public bool CollectAllSessionStats { get; set; } = false;
        public bool PrintAllSessionStats { get; set; } = false;

        /* dict key is DataID of DataType class to filter */
        public Dictionary<string, FilterDef> FiltersInbound { get; set; } = new ();
        public Dictionary<string, FilterDef> FiltersOutbound { get; set; } = new ();

        public class FilterDef {
            public int DropThreshold { get; set; } = -1;
            public int DropLimit { get; set; } = -1;
            public int DropChance { get; set; } = 100;

        }

    }
}
