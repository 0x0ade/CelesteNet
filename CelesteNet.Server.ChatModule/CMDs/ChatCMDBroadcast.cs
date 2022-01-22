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
    public class ChatCMDBC : ChatCMDBroadcast {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<ChatCMDBroadcast>().ID}";

    }

    public class ChatCMDBroadcast : ChatCMD {

        public override string Args => "<text>";

        public override string Info => "Send a server broadcast to everyone in the server, as the server.";

        public override bool MustAuth => true;

        public override void Run(ChatCMDEnv env, List<ChatCMDArg> args) {
            if (args.Count == 0)
                throw new Exception("No text.");

            Chat.Broadcast(args[0].Rest);
        }

    }
}
