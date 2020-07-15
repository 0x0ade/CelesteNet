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
    public class ChatCMDW : ChatCMDWhisper {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<ChatCMDWhisper>().ID}";

    }

    public class ChatCMDWhisper : ChatCMD {

        public override string Args => "<user> <text>";

        public override string Info => "Send a whisper to someone else.";

        public override void Run(ChatCMDEnv env, params ChatCMDArg[] args) {
            if (args.Length == 0)
                throw new Exception("No username.");

            if (args.Length == 1)
                throw new Exception("No text.");

            CelesteNetPlayerSession? other = args[0].Session;
            DataPlayerInfo otherPlayer = other?.PlayerInfo ?? throw new Exception("Invalid username or ID.");

            string text = args[1].Rest;

            DataPlayerInfo? player = env.Player;
            if (player != null) {
                env.Msg.Tag = $"whisper @ {otherPlayer.DisplayName}";
                env.Msg.Text = text;
                env.Msg.Color = Chat.Settings.ColorWhisper;
                Chat.ForceSend(env.Msg);
            }

            other.Con.Send(Chat.PrepareAndLog(null, new DataChat {
                Player = player,
                Target = otherPlayer,
                Tag = "whisper",
                Text = text,
                Color = Chat.Settings.ColorWhisper
            }));
        }

    }
}
