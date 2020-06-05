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

                /*
                if (!string.IsNullOrWhiteSpace(player.Value.SID)) {
                    builder
                        .Append(" @ ")
                        .Append(Escape(AreaDataExt.Get(player.Value.SID)?.Name?.DialogCleanOrNull(Dialog.Languages["english"]) ?? player.Value.SID))
                        .Append(" ")
                        .Append((char) ('A' + (int) player.Value.Mode))
                        .Append(" ")
                        .Append(Escape(player.Value.Level))
                    ;
                }
                */

                builder.AppendLine();
            }

            ListText = builder.ToString().Trim();
        }

        public void Handle(CelesteNetConnection con, DataChat msg) {
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
            if ((global::Celeste.Settings.Instance?.SpeedrunClock ?? SpeedrunType.Off) != SpeedrunType.Off)
                y += 192f;

            Vector2 size = ActiveFont.Measure(ListText) * fontScale;
            MDraw.Rect(25f * scale, y - 25f * scale, size.X + 50f * scale, size.Y + 50f * scale, Color.Black * 0.8f);
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
