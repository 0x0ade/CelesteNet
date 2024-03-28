using System.Collections.Generic;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdGC : CmdGlobalChat {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdGlobalChat>().ID}";

    }

    public class CmdGlobalChat : ChatCmd {

        public override string Info => "Send a message to everyone in the server.";

        public override string Help =>
$@"Send a message to everyone in the server.
To send a message, {Chat.Settings.CommandPrefix}{ID} message here
To enable / disable auto channel chat mode, {Chat.Settings.CommandPrefix}{ID}";

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamString(chat, null, ParamFlags.Optional), "message", "Hi to global chat!");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg>? args) {
            if (env.Session == null)
                return;

            if (args == null || args.Count == 0 || args[0] is not CmdArgString argMsg || string.IsNullOrEmpty(argMsg.String)) {
                // without arguments this is just toggle mode, same implementation as "/cc" with no args.
                Chat.Commands.Get<CmdChannelChat>().Run(env, args);
                return;
            }

            if (env.Player == null)
                return;

            DataChat? msg = Chat.PrepareAndLog(null, new DataChat {
                Player = env.Player,
                Text = argMsg
            });

            if (msg == null)
                return;

            env.Msg.Text = argMsg;
            env.Msg.Tag = "";
            env.Msg.Color = Color.White;
            env.Msg.Targets = null;
            Chat.ForceSend(env.Msg);
        }

    }
}
