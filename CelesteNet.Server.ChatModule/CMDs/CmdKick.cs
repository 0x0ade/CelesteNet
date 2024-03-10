using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdKickQ : CmdKick {

        public override string Info => $"Same as {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdKick>().ID} but without Broadcast (quiet)";

        public override bool InternalAliasing => true;

        public override bool Quiet => true;
    }

    public class CmdKick : ChatCmd {

        public override string Args => "<user>";

        public override string Info => "Kick a player from the server.";

        public override CompletionType Completion => CompletionType.Player;

        public override bool MustAuth => true;

        public virtual bool Quiet => false;

        public override void Run(CmdEnv env, List<CmdArg> args) {
            if (args.Count == 0)
                throw new Exception("No user.");

            CelesteNetPlayerSession player = args[0].Session ?? throw new Exception("Invalid username or ID.");
            if (!Quiet)
                new DynamicData(player).Set("leaveReason", Chat.Settings.MessageKick);
            player.Dispose();
            player.Con.Send(new DataDisconnectReason { Text = "Kicked" });
            player.Con.Send(new DataInternalDisconnect());
        }

    }
}
