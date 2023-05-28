using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatCMDStatus : ChatCMD {

        public override string Args => "<text>";

        public override string Info => "Set the server status, shown as the spinning cogwheel for everyone.";

        public override bool MustAuth => true;

        public override void Run(ChatCMDEnv env, List<ChatCMDArg> args) {
            if (args.Count == 0)
                throw new Exception("No text.");

            env.Server.BroadcastAsync(new DataServerStatus {
                Text = args[0].Rest
            });
        }

    }
}
