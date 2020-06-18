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
    public class ChatCMDJoin : ChatCMDChannel {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}channel";

    }

    public class ChatCMDChannel : ChatCMD {

        public override string Args => "[page] | [channel]";

        public override string Info => "Switch to a different channel.";
        public override string Help =>
$@"Switch to a different channel.
Work in progress, might not work properly.
To list all public channels, {Chat.Settings.CommandPrefix}{ID}
To create / join a public channel, {Chat.Settings.CommandPrefix}{ID} channel
To create / join a private channel, {Chat.Settings.CommandPrefix}{ID} {Channels.PrefixPrivate}channel
To go back to the default channel, {Chat.Settings.CommandPrefix}{ID} {Chat.Server.Channels.Default.Name}";

        public override void ParseAndRun(ChatCMDEnv env) {
            CelesteNetPlayerSession? session = env.Session;
            DataPlayerState? state = env.State;
            if (session == null || state == null)
                return;

            Channels channels = env.Server.Channels;

            Channel? c;

            if (int.TryParse(env.Text, out int page) ||
                string.IsNullOrWhiteSpace(env.Text)) {

                if (channels.All.Count == 0) {
                    env.Send($"No channels. See {Chat.Settings.CommandPrefix}{ID} on how to create one.");
                    return;
                }

                const int pageSize = 8;

                StringBuilder builder = new StringBuilder();

                int pages = (int) Math.Ceiling(channels.All.Count / (float) pageSize);
                if (page < 0 || pages <= page)
                    throw new Exception("Page out of range!");

                if (page == 0)
                    builder
                        .Append("You're in ")
                        .Append(channels.Get(session).Name)
                        .AppendLine();

                for (int i = page * pageSize; i < (page + 1) * pageSize && i < channels.All.Count; i++) {
                    c = channels.All[i];
                    builder
                        .Append(c.PublicName)
                        .Append(" - ")
                        .Append(c.Players.Count)
                        .Append(" players")
                        .AppendLine();
                }

                builder
                    .Append("Page ")
                    .Append(page + 1)
                    .Append("/")
                    .Append(pages);

                env.Send(builder.ToString().Trim());

                return;
            }

            Tuple<Channel, Channel> tuple = channels.Move(session, env.Text);
            if (tuple.Item1 == tuple.Item2) {
                env.Send($"Already in {tuple.Item2.Name}");
            } else {
                env.Send($"Moved to {tuple.Item2.Name}");
            }
        }

    }
}
