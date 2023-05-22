using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatSettings : CelesteNetServerModuleSettings {

        public int MaxChatTextLength { get; set; } = 256;

        public string CommandPrefix { get; set; } = "/";

        public bool GreetPlayers { get; set; } = true;
        public bool LogEmotes { get; set; } = false;

        public int SpamCount { get; set; } = 3;
        public int SpamCountMax { get; set; } = 4;
        public double SpamTimeout { get; set; } = 4;
        public double SpamTimeoutAdd { get; set; } = 5;

        public Color ColorBroadcast { get; set; } = ColorHelpers.HexToColor("#00adee");
        public Color ColorServer { get; set; } = ColorHelpers.HexToColor("#9e24f5");
        public Color ColorError { get; set; } = ColorHelpers.HexToColor("#c71585");
        public Color ColorCommand { get; set; } = ColorHelpers.HexToColor("#2e31f1");
        public Color ColorCommandReply { get; set; } = ColorHelpers.HexToColor("#e39dcc");
        public Color ColorWhisper { get; set; } = ColorHelpers.HexToColor("#888888");
        public Color ColorLogEmote { get; set; } = ColorHelpers.HexToColor("#bbbb88");

        public string MessageGreeting { get; set; } = "Welcome {player}, to <insert server name here>!";
        public string MessageMOTD { get; set; } =
@"Don't cheat. Have fun!
Press T to talk.
Send /help for a list of all commands.";
        public string MessageLeave { get; set; } = "Cya, {player}!";
        public string MessageKick { get; set; } = "{player} did an oopsie!";
        public string MessageBan { get; set; } = "{player} won't come back.";
        public string MessageSpam { get; set; } = "Stop spamming.";

    }
}
