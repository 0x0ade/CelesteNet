using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdBC : CmdBroadcast {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdBroadcast>().ID}";

    }

    public class CmdBroadcast : ChatCmd {

        public override string Args => "<text>";

        public override string Info => "Send a server broadcast to everyone in the server, as the server.";

        public override bool MustAuth => true;

        public override void Run(CmdEnv env, List<CmdArg> args) {
            if (args.Count == 0)
                throw new Exception("No text.");

            Chat.Broadcast(args[0].Rest);
        }

    }
}
