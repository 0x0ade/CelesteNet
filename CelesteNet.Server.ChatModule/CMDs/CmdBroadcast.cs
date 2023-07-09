using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdBC : CmdBroadcast {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdBroadcast>().ID}";

    }

    public class CmdBroadcast : ChatCmd {

        public override string Info => "Send a server broadcast to everyone in the server, as the server.";

        public override bool MustAuth => true;

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamString(chat), "message", "Announcement: meow.");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg>? args) {
            if (args == null || args.Count == 0 || args[0] is not CmdArgString argMsg)
                throw new CommandRunException("No text.");

            Chat.Broadcast(argMsg);
        }

    }
}
