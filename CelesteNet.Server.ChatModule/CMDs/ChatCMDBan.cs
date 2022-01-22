using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatCMDBan : ChatCMD {

        public override string Args => "<user> <text>";

        public override string Info => "Ban a player from the server with a given reason.";

        public override bool MustAuth => true;

        public override void Run(ChatCMDEnv env, List<ChatCMDArg> args) {
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

            ChatModule chat = env.Chat.Server.Get<ChatModule>();
            new DynamicData(player).Set("leaveReason", chat.Settings.MessageBan);
            player.Dispose();
            player.Con.Send(new DataDisconnectReason { Text = "Banned: " + ban.Reason });
            player.Con.Send(new DataInternalDisconnect());

            env.Chat.Server.UserData.Save(player.UID, ban);
        }

    }
}
