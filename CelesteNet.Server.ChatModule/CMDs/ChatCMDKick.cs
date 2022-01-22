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
    public class ChatCMDKick : ChatCMD {

        public override string Args => "<user>";

        public override string Info => "Kick a player from the server.";

        public override bool MustAuth => true;

        public override void Run(ChatCMDEnv env, List<ChatCMDArg> args) {
            if (args.Count == 0)
                throw new Exception("No user.");

            CelesteNetPlayerSession player = args[0].Session ?? throw new Exception("Invalid username or ID.");

            new DynamicData(player).Set("leaveReason", env.Chat.Settings.MessageKick);
            player.Dispose();
            player.Con.Send(new DataDisconnectReason { Text = "Kicked" });
            player.Con.Send(new DataInternalDisconnect());
        }

    }
}
