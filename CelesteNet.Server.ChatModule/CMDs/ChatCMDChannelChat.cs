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
    public class ChatCMDCC : ChatCMDChannelChat {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<ChatCMDChannelChat>().ID}";

    }

    public class ChatCMDChannelChat : ChatCMD {

        public override string Args => "<text>";

        public override string Info => "Send a whisper to everyone in the channel.";

        public override void ParseAndRun(ChatCMDEnv env) {
            CelesteNetPlayerSession? session = env.Session;
            if (session == null)
                return;

            DataPlayerInfo? player = env.Player;
            string text = env.Text.Trim();
            Channel channel = env.Server.Channels.Get(session);

            CelesteNetPlayerSession[] others = channel.Players.Where(p => p != session).ToArray();

            DataChat? msg = Chat.PrepareAndLog(null, new DataChat {
                Player = player,
#pragma warning disable CS8619 // LINQ is dumb.
                Targets = others.Select(p => p.PlayerInfo).Where(p => p != null).ToArray(),
#pragma warning restore CS8619
                Tag = $"channel {channel.Name}",
                Text = text,
                Color = Chat.Settings.ColorWhisper
            });

            if (msg == null)
                return;

            if (player != null) {
                env.Msg.Tag = $"channel {channel.Name}";
                env.Msg.Text = text;
                env.Msg.Color = Chat.Settings.ColorWhisper;
                Chat.ForceSend(env.Msg);
            }

            if ((msg.Targets?.Length ?? 0) == 0)
                return;

            DataInternalBlob blob = new DataInternalBlob(env.Server.Data, msg);
            foreach (CelesteNetPlayerSession other in others)
                other.Con.Send(blob);
        }

    }
}
