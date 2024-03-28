using System;
using System.Collections.Generic;
using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {

    public class CmdBanQ : CmdBan {

        public override string Info => $"Same as {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdBan>().ID} but without Broadcast (quiet)";

        public override bool InternalAliasing => true;

        public override bool Quiet => true;
    }

    public class CmdBan : ChatCmd {

        public override CompletionType Completion => CompletionType.Player;

        public override string Info => "Ban a player from the server with a given reason.";

        public override bool MustAuth => true;

        public virtual bool Quiet => false;

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamPlayerSession(chat));
            parser.AddParameter(new ParamString(chat), "reason", "Naughty player.");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg>? args) {
            if (args == null || args.Count == 0)
                throw new CommandRunException("No user.");

            if (args.Count < 2)
                throw new CommandRunException("No text.");

            if (args[0] is not CmdArgPlayerSession sessionArg)
                throw new CommandRunException("Invalid username or ID.");

            CelesteNetPlayerSession player = sessionArg.Session ?? throw new CommandRunException("Invalid username or ID.");

            string? banReason = args[1].ToString();

            if (banReason.IsNullOrEmpty())
                throw new CommandRunException("No reason given.");

            BanInfo ban = new() {
                UID = player.UID,
                Name = player.PlayerInfo?.FullName ?? "",
                Reason = banReason,
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
