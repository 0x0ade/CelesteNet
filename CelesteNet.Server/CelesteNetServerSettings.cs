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

        public int MainPort { get; set; } = 3802;

        public int ControlPort { get; set; } = 3800;
        public string ControlPassword { get; set; } = "actuallyHosts";

        public string ContentRoot { get; set; } = "Content";

        public LogLevel LogLevel { get; set; } = Logger.Level;

        public int MaxNameLength { get; set; } = 16;
        public int MaxEmoteValueLength { get; set; } = 2048;
        public int MaxChatTextLength { get; set; } = 256;

        public string CommandPrefix { get; set; } = "/";

        public Color ColorBroadcast { get; set; } = Calc.HexToColor("#00adee");
        public Color ColorServer { get; set; } = Calc.HexToColor("#9e24f5");

        public string MessageGreeting { get; set; } = "Welcome {player}, to <insert server name here>!";
        public string MessageMOTD { get; set; } =
@"Don't cheat. Have fun!
Press T to talk.
Send /help for a list of all commands.";
        public string MessageLeave { get; set; } = "Cya, {player}!";

    }
}
