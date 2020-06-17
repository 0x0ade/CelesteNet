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
    // FIXME: MOVE CHANNEL MANAGEMENT OUT OF HERE!
    public class ChatCMDChannel : ChatCMD {

        public const string DefaultName = "main";

        public override string Args => "[page] | [channel]";

        public override string Info => "Switch to a different channel.";
        public override string Help =>
$@"Switch to a different channel.
Work in progress, might not work properly.
To list all public channels, {Chat.Settings.CommandPrefix}{ID}
To create / join a public channel, {Chat.Settings.CommandPrefix}{ID} channel
To create / join a private channel, {Chat.Settings.CommandPrefix}{ID} #channel
To go back to the default channel, {Chat.Settings.CommandPrefix}{ID} {DefaultName}";

        public readonly List<Channel> All = new List<Channel>();
        public readonly Dictionary<uint, Channel> ByID = new Dictionary<uint, Channel>();
        public readonly Dictionary<string, Channel> ByName = new Dictionary<string, Channel>();
        public uint NextID = (uint) (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);

        public override void ParseAndRun(ChatCMDEnv env) {
            CelesteNetPlayerSession? session = env.Session;
            DataPlayerState? state = env.State;
            if (session == null || state == null)
                return;

            Channel? c;

            if (int.TryParse(env.Text, out int page) ||
                string.IsNullOrWhiteSpace(env.Text)) {

                if (All.Count == 0) {
                    env.Send($"No channels. See {Chat.Settings.CommandPrefix}{ID} on how to create one.");
                    return;
                }

                const int pageSize = 8;

                StringBuilder builder = new StringBuilder();

                int pages = (int) Math.Ceiling(All.Count / (float) pageSize);
                if (page < 0 || pages <= page)
                    throw new Exception("Page out of range!");

                if (page == 0)
                    builder
                        .Append("You're in ")
                        .Append(state.Channel == 0 ? DefaultName : ByID.TryGetValue(state.Channel, out c) ? c.Name : $"?{state.Channel}?")
                        .AppendLine();

                for (int i = page * pageSize; i < (page + 1) * pageSize && i < All.Count; i++) {
                    c = All[i];
                    builder
                        .Append(c.Name.StartsWith("#") ? "########" : c.Name)
                        .Append(" - ")
                        .Append(c.Players)
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

            string name = env.Text.Trim();

            lock (All) {
                if (ByID.TryGetValue(state.Channel, out Channel? prev) && prev.ID != state.Channel)
                    prev.Remove(session);

                if (name == DefaultName) {
                    state.Channel = 0;

                } else if (ByName.TryGetValue(name, out c)) {
                    if (state.Channel == c.ID) {
                        env.Send($"Already in {name}");
                        return;
                    }
                    state.Channel = c.ID;
                    c.Add(session);

                } else {
                    c = new Channel(this, name, NextID++);
                    state.Channel = c.ID;
                    c.Add(session);
                }

                session.Con.Send(state);
                env.Server.Data.Handle(session.Con, state);

                env.Send($"Switched to {name}");
            }
        }

        public class Channel {
            public ChatCMDChannel Ctx;
            public string Name;
            public uint ID;
            public int Players;
            
            public Channel(ChatCMDChannel ctx, string name, uint id) {
                Ctx = ctx;
                Name = name;
                ID = id;

                lock (Ctx.All) {
                    Ctx.All.Add(this);
                    Ctx.ByName[Name] = this;
                    Ctx.ByID[ID] = this;
                }
            }

            public void Add(CelesteNetPlayerSession session) {
                session.OnEnd += RemoveByDC;
                Players++;
            }

            public void Remove(CelesteNetPlayerSession session) {
                session.OnEnd -= RemoveByDC;
                if ((--Players) > 0)
                    return;

                lock (Ctx.All) {
                    Ctx.All.Remove(this);
                    Ctx.ByName.Remove(Name);
                    Ctx.ByID.Remove(ID);
                }
            }

            private void RemoveByDC(CelesteNetPlayerSession session, DataPlayerInfo? lastInfo) {
                Remove(session);
            }
        }

    }
}
