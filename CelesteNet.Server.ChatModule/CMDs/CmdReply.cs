using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {

    public class CmdR : CmdReply {
        public override string Info => $"Alias for {Chat.Commands.Get<CmdReply>().InvokeString}";
    }

    public class CmdReply : ChatCmd {

        public override string Info => "Reply to the most recent whisper.";

        public override string Help =>
$@"Reply to the most recent whisper.
Sends a whisper to recipient or sender of the most recent whisper.";

        private ParamPlayerSession? playerSessionParam;

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamString(chat), "message", "I'm replying!");
            ArgParsers.Add(parser);

            playerSessionParam = new ParamPlayerSession(Chat);
        }

        public override void Run(CmdEnv env, List<ICmdArg>? args) {
            if (env.Session == null)
                return;

            uint lastWhisperID = env.Session.LastWhisperSessionID;

            if (lastWhisperID == uint.MaxValue)
                throw new CommandRunException("You have not sent or received any whispers since you last connected.");

            if (args == null || args.Count == 0 || args[0] is not CmdArgString)
                throw new CommandRunException("No text.");

            playerSessionParam ??= new ParamPlayerSession(Chat);

            bool parsedSessionID = playerSessionParam.TryParse(lastWhisperID.ToString(), env, out ICmdArg? sessionArg);

            if (!parsedSessionID || sessionArg == null || sessionArg is not CmdArgPlayerSession arg || arg.Session == null)
                throw new CommandRunException("The player has disconnected from the server.");

            args.Insert(0, arg);

            Chat.Commands.Get<CmdWhisper>().Run(env, args);
        }
    }
}

