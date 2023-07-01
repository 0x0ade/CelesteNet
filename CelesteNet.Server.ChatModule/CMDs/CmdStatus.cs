using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdStatus : ChatCmd {

        public override string Args => "<text>";

        public override string Info => "Set the server status, shown as the spinning cogwheel for everyone.";

        public override bool MustAuth => true;

        public override void Run(CmdEnv env, List<CmdArg> args) {
            if (args.Count == 0)
                throw new Exception("No text.");

            env.Server.BroadcastAsync(new DataServerStatus {
                Text = args[0].Rest
            });
        }

    }
}
