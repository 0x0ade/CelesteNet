using System.Collections.Generic;
using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdKickQ : CmdKick {

        public override string Info => $"Same as {Chat.Commands.Get<CmdKick>().InvokeString} but without Broadcast (quiet)";

        public override bool InternalAliasing => true;

        public override bool Quiet => true;
    }

    public class CmdKick : ChatCmd {

        public override string Info => "Kick a player from the server.";

        public override CompletionType Completion => CompletionType.Player;

        public override bool MustAuth => true;

        public virtual bool Quiet => false;

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamPlayerSession(chat));
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg>? args) {
            if (args == null || args.Count == 0)
                throw new CommandRunException("No user.");

            if (args[0] is not CmdArgPlayerSession sessionArg) {
                throw new CommandRunException("Invalid username or ID.");
            }

            CelesteNetPlayerSession player = sessionArg.Session ?? throw new CommandRunException("Invalid username or ID.");

            if (!Quiet)
                new DynamicData(player).Set("leaveReason", Chat.Settings.MessageKick);
            player.Con.Send(new DataDisconnectReason { Text = Chat.Settings.MessageDefaultKickReason });
            player.Con.Send(new DataInternalDisconnect());
            player.Dispose();
        }

    }
}
