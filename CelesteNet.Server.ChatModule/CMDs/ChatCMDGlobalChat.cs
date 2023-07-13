using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatCMDGC : ChatCMDGlobalChat {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<ChatCMDGlobalChat>().ID}";

    }

    public class ChatCMDGlobalChat : ChatCMD {

        public override string Args => "<text>";

        public override string Info => "Send a message to everyone in the server.";

        public override string Help =>
$@"Send a message to everyone in the server.
To send a message, {Chat.Settings.CommandPrefix}{ID} message here
To enable / disable auto channel chat mode, {Chat.Settings.CommandPrefix}{ID}";

        public override void ParseAndRun(ChatCMDEnv env) {
            CelesteNetPlayerSession? session = env.Session;
            if (session == null)
                return;

            string text = env.Text.Trim();

            if (string.IsNullOrEmpty(text)) {
                Chat.Commands.Get<ChatCMDChannelChat>().ParseAndRun(env);
                return;
            }

            DataPlayerInfo? player = env.Player;
            if (player == null)
                return;

            DataChat? msg = Chat.PrepareAndLog(null, new DataChat {
                Player = player,
                Text = text
            });

            if (msg == null)
                return;

            env.Msg.Text = text;
            env.Msg.Tag = "";
            env.Msg.Color = Color.White;
            env.Msg.Target = null;
            Chat.ForceSend(env.Msg);
        }

    }
}
