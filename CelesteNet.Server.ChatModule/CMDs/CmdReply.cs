using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {

    public class CmdR : CmdReply {
        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdReply>().ID}";
    }

    public class CmdReply : ChatCmd {
        public override string Args => "<text>";

        public override string Info => "Reply to the most recent whisper.";

        public override string Help =>
$@"Reply to the most recent whisper.
Sends a whisper to recipient or sender of the most recent whisper.";

        public override void Run(CmdEnv env, List<CmdArg> args) {
            if (env.Session == null)
                return;

            if (env.Session.LastWhisperSessionID == uint.MaxValue)
                throw new Exception("You have not sent or received any whispers since you last connected.");

            if (args.Count == 0)
                throw new Exception("No text.");

            CmdArg sessionArg = new CmdArg(env).Parse(env.Session.LastWhisperSessionID.ToString(), 0);

            if (sessionArg.Session == null)
                throw new Exception("The player has disconnected from the server.");

            args.Insert(0, sessionArg);

            Chat.Commands.Get<CmdWhisper>().Run(env, args);
        }
    }
}

