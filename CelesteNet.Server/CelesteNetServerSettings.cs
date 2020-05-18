using Mono.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetServerSettings {

        public int MainPort { get; set; } = 3802;

        public int ControlPort { get; set; } = 3800;

        public string ContentRoot { get; set; } = "Content";

        public LogLevel LogLevel { get; set; } = Logger.Level;

    }
}
