using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdStatus : ChatCmd {

        public override string Info => "Set the server status, shown as the spinning cogwheel for everyone.";

        public override bool MustAuth => true;

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamString(chat), "message", "The cog must spin...");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg> args) {
            if (args.Count == 0 || args[0] is not CmdArgString argMsg)
                throw new CommandRunException("No text.");

            env.Server.BroadcastAsync(new DataServerStatus {
                Text = argMsg
            });
        }

    }
}
