using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetPlayerListComponent : CelesteNetGameComponent {

        public float Scale => 0.5f + 0.5f * ((Settings.UISize - 1f) / (CelesteNetClientSettings.UISizeMax - 1f));

        public bool Active;

        private string ListText;

        public DataChannelList Channels;

        public ListMode Mode => Settings.PlayerListMode;
        private ListMode LastMode;

        public enum ListMode {
            Channels,
            Classic,
        }

        public CelesteNetPlayerListComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10001;
        }

        public void RebuildList() {
            if (MDraw.DefaultFont == null || Client == null || Channels == null)
                return;

            StringBuilder builder = new StringBuilder();


            switch (Mode) {
                case ListMode.Classic:
                    foreach (DataPlayerInfo player in Client.Data.GetRefs<DataPlayerInfo>()) {
                        if (string.IsNullOrWhiteSpace(player.DisplayName))
                            continue;

                        builder.Append(player.DisplayName);

                        DataChannelList.Channel channel = Channels.List.FirstOrDefault(c => c.Players.Contains(player.ID));
                        if (channel != null && !string.IsNullOrEmpty(channel.Name)) {
                            builder
                                .Append(" #")
                                .Append(channel.Name);
                        }

                        if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                            AppendState(builder, state);

                        builder.AppendLine();
                    }
                    break;

                case ListMode.Channels:
                    HashSet<DataPlayerInfo> listed = new HashSet<DataPlayerInfo>();

                    foreach (DataChannelList.Channel channel in Channels.List) {
                        builder
                            .Append(channel.Name)
                            .AppendLine();

                        foreach (uint playerID in channel.Players)
                            listed.Add(ListPlayerUnderChannel(builder, playerID));
                    }

                    bool wrotePrivate = false;

                    foreach (DataPlayerInfo player in Client.Data.GetRefs<DataPlayerInfo>()) {
                        if (listed.Contains(player) || string.IsNullOrWhiteSpace(player.DisplayName))
                            continue;

                        if (!wrotePrivate) {
                            wrotePrivate = true;
                            builder.AppendLine();
                        }

                        builder.AppendLine(player.DisplayName);
                    }
                    break;
            }

            ListText = builder.ToString().Trim();
        }

        private DataPlayerInfo ListPlayerUnderChannel(StringBuilder builder, uint playerID) {
            if (Client.Data.TryGetRef(playerID, out DataPlayerInfo player) && !string.IsNullOrEmpty(player.DisplayName)) {
                builder
                    .Append("  ")
                    .Append(player.DisplayName);

                if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                    AppendState(builder, state);

                builder.AppendLine();
                return player;

            } else {
                builder.AppendLine("  ?");
                return null;
            }
        }

        private void AppendState(StringBuilder builder, DataPlayerState state) {
            if (!string.IsNullOrWhiteSpace(state.SID))
                builder
                    .Append(" @ ")
                    .Append(AreaDataExt.Get(state.SID)?.Name?.DialogCleanOrNull(Dialog.Languages["english"]) ?? state.SID)
                    .Append(" ")
                    .Append((char) ('A' + (int) state.Mode))
                    .Append(" ")
                    .Append(state.Level);
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo info) {
            RebuildList();
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            RebuildList();
        }

        public void Handle(CelesteNetConnection con, DataChannelList channels) {
            Channels = channels;
            RebuildList();
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (LastMode != Mode) {
                LastMode = Mode;
                RebuildList();
            }

            if (!(Engine.Scene?.Paused ?? false) && Settings.ButtonPlayerList.Button.Pressed)
                Active = !Active;
        }

        public override void Draw(GameTime gameTime) {
            if (Active)
                base.Draw(gameTime);
        }

        protected override void Render(GameTime gameTime, bool toBuffer) {
            if (ListText == null)
                RebuildList();
            if (ListText == null)
                return;

            float scale = Scale;
            Vector2 fontScale = Vector2.One * scale;

            float y = 50f * scale;

            SpeedrunTimerDisplay timer = Engine.Scene?.Entities.FindFirst<SpeedrunTimerDisplay>();
            if (timer != null) {
                switch (global::Celeste.Settings.Instance?.SpeedrunClock ?? SpeedrunType.Off) {
                    case SpeedrunType.Off:
                        break;

                    case SpeedrunType.Chapter:
                        if (timer.CompleteTimer < 3f)
                            y += 104f;
                        break;

                    case SpeedrunType.File:
                    default:
                        y += 104f + 24f;
                        break;
                }
            }

            Vector2 size = CelesteNetClientFont.Measure(ListText) * fontScale;
            Context.Blur.Rect(25f * scale, y - 25f * scale, size.X + 50f * scale, size.Y + 50f * scale, Color.Black * 0.8f);
            CelesteNetClientFont.Draw(
                ListText,
                new Vector2(50f * scale, y),
                Vector2.Zero,
                fontScale,
                Color.White
            );
        }

    }
}
