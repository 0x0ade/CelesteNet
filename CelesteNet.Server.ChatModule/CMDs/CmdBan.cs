using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {

    public class CmdBanQ : CmdBan {

        public override string Info => $"Same as {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdBan>().ID} but without Broadcast (quiet)";

        public override bool InternalAliasing => true;

        public override bool Quiet => true;
    }

    public class CmdBan : ChatCmd {

        public override string Args => "<user> <text>";

        public override CompletionType Completion => CompletionType.Player;

        public override string Info => "Ban a player from the server with a given reason.";

        public override bool MustAuth => true;

        public virtual bool Quiet => false;

        public override void Run(CmdEnv env, List<CmdArg> args) {
            if (args.Count == 0)
                throw new Exception("No user.");

            if (args.Count == 1)
                throw new Exception("No text.");

            CelesteNetPlayerSession player = args[0].Session ?? throw new Exception("Invalid username or ID.");

            BanInfo ban = new() {
                UID = player.UID,
                Name = player.PlayerInfo?.FullName ?? "",
                Reason = args[1].Rest,
                From = DateTime.UtcNow
            };

            ChatModule chat = env.Server.Get<ChatModule>();

            if (!Quiet)
                new DynamicData(player).Set("leaveReason", chat.Settings.MessageBan);
            player.Dispose();
            player.Con.Send(new DataDisconnectReason { Text = "Banned: " + ban.Reason });
            player.Con.Send(new DataInternalDisconnect());

            env.Server.UserData.Save(player.UID, ban);
        }

    }
}
