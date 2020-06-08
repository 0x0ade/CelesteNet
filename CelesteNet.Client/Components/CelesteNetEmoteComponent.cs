using Celeste.Mod.CelesteNet.Client.Entities;
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
    public class CelesteNetEmoteComponent : CelesteNetGameComponent {

        public Player Player;
        public GhostEmoteWheel Wheel;

        public CelesteNetEmoteComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;
        }

        public void Send(string text) {
            Client.SendAndHandle(new DataEmote {
                Player = Client.PlayerInfo,
                Text = text?.Trim()
            });
        }

        public void Handle(CelesteNetConnection con, DataEmote emoteData) {
            Entity target;

            if (emoteData.Player.ID == Client.PlayerInfo.ID) {
                target = Player;
            } else if (Context.Main.Ghosts.TryGetValue(emoteData.Player.ID, out Ghost ghost)) {
                target = ghost;
            } else {
                return;
            }

            target.Scene?.Add(new GhostEmote(target, emoteData.Text) {
                PopIn = true,
                FadeOut = true
            });
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (Client == null || !Client.IsReady)
                return;

            if (!(Engine.Scene is Level level))
                return;

            if (Player == null || Player.Scene != level)
                Player = level.Tracker.GetEntity<Player>();

            if (Wheel != null && (Wheel.Scene != level || Wheel.Tracking != Player)) {
                Wheel?.RemoveSelf();
                Wheel = null;
            }

            if (Player == null)
                return;

            if (Wheel == null)
                level.Add(Wheel = new GhostEmoteWheel(Player));

            if (!level.Paused) {
                Wheel.Shown = CelesteNetClientModule.Instance.JoystickEmoteWheel.Value.LengthSquared() >= 0.36f;
                int selected = Wheel.Selected;
                if (Wheel.Shown && selected != -1 && CelesteNetClientModule.Instance.ButtonEmoteSend.Pressed) {
                    string[] emotes = CelesteNetClientModule.Settings.Emotes;
                    if (0 <= selected && selected < emotes.Length) {
                        Send(emotes[selected]);
                    }
                }
            } else {
                Wheel.Shown = false;
                Wheel.Selected = -1;
            }
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            Wheel?.RemoveSelf();
        }

    }
}
