using Microsoft.Xna.Framework;
using Mono.Options;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetServerSettings {

        public string ModuleRoot { get; set; } = "Modules";
        public string ModuleConfigRoot { get; set; } = "ModuleConfigs";
        public string UserDataRoot { get; set; } = "UserData";

        public int MainPort { get; set; } = 3802;

        public LogLevel LogLevel { get; set; } = Logger.Level;

        public int MaxNameLength { get; set; } = 16;
        public int MaxEmoteValueLength { get; set; } = 2048;

    }
}
