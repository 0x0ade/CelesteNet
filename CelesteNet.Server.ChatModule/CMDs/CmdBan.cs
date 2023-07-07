using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using YamlDotNet.Core;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdBan : ChatCmd {

        public override CompletionType Completion => CompletionType.Player;

        public override string Info => "Ban a player from the server with a given reason.";

        public override bool MustAuth => true;

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamPlayerSession(chat));
            parser.AddParameter(new ParamString(chat), "reason", "Naughty player.");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg> args) {
            if (args.Count == 0)
                throw new CommandRunException("No user.");

            if (args.Count == 1)
                throw new CommandRunException("No text.");

            if (args[0] is not CmdArgPlayerSession sessionArg) {
                throw new CommandRunException("Invalid username or ID.");
            }

            CelesteNetPlayerSession player = sessionArg.Session ?? throw new CommandRunException("Invalid username or ID.");

            BanInfo ban = new() {
                UID = player.UID,
                Name = player.PlayerInfo?.FullName ?? "",
                Reason = args[1].ToString(),
                From = DateTime.UtcNow
            };

            ChatModule chat = env.Server.Get<ChatModule>();
            new DynamicData(player).Set("leaveReason", chat.Settings.MessageBan);
            player.Dispose();
            player.Con.Send(new DataDisconnectReason { Text = "Banned: " + ban.Reason });
            player.Con.Send(new DataInternalDisconnect());

            env.Server.UserData.Save(player.UID, ban);
        }

    }
}
