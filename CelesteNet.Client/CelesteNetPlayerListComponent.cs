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

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetPlayerListComponent : CelesteNetGameComponent {

        public float Scale => 0.5f + 0.5f * ((Settings.UISize - 1f) / (CelesteNetClientSettings.UISizeMax - 1f));

        public bool Active;

        private string ListText;

        public CelesteNetPlayerListComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10001;
        }

        public void RebuildList() {
            if (MDraw.DefaultFont == null || Client == null)
                return;

            StringBuilder builder = new StringBuilder();
            foreach (DataPlayerInfo player in Client.Data.GetRefs<DataPlayerInfo>()) {
                if (string.IsNullOrWhiteSpace(player.FullName))
                    continue;

                builder.Append(player.FullName);

                if (Client.Data.TryGetBoundRef(player, out DataPlayerState state) &&
                    !string.IsNullOrWhiteSpace(state.SID)) {
                    builder
                        .Append(" @ ")
                        .Append(AreaDataExt.Get(state.SID)?.Name?.DialogCleanOrNull(Dialog.Languages["english"]) ?? state.SID)
                        .Append(" ")
                        .Append((char) ('A' + (int) state.Mode))
                        .Append(" ")
                        .Append(state.Level)
                    ;
                }

                builder.AppendLine();
            }

            ListText = builder.ToString().Trim();
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo info) {
            RebuildList();
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            RebuildList();
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

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

            Vector2 size = ActiveFont.Measure(ListText) * fontScale;
            Context.Blur.Rect(25f * scale, y - 25f * scale, size.X + 50f * scale, size.Y + 50f * scale, Color.Black * 0.7f);
            ActiveFont.Draw(
                ListText,
                new Vector2(50f * scale, y),
                Vector2.Zero,
                fontScale,
                Color.White
            );
        }

    }
}
