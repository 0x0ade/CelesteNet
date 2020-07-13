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

        public CelesteNetEmoteComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;
        }

        public override void Initialize() {
            base.Initialize();

            MainThreadHelper.Do(() => {
                On.Celeste.HeartGem.Collect += OnHeartGemCollect;
                On.Celeste.HeartGem.EndCutscene += OnHeartGemEndCutscene;
                On.Celeste.Player.Die += OnPlayerDie;
            });
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            Wheel?.RemoveSelf();

            MainThreadHelper.Do(() => {
                On.Celeste.HeartGem.Collect -= OnHeartGemCollect;
                On.Celeste.HeartGem.EndCutscene -= OnHeartGemEndCutscene;
                On.Celeste.Player.Die -= OnPlayerDie;
            });
        }

        public void Send(string text) {
            Client.SendAndHandle(new DataEmote {
                Player = Client.PlayerInfo,
                Text = text?.Trim()
            });
        }

        public void Send(int index) {
            string[] emotes = CelesteNetClientModule.Settings.Emotes;
            if (0 <= index && index < emotes.Length)
                Send(emotes[index]);
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

            target?.Scene?.Add(new GhostEmote(target, emoteData.Text) {
                PopIn = true,
                FadeOut = true
            });
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (Client == null || !Client.IsReady)
                goto End;

            if (!(Engine.Scene is Level level))
                goto End;

            if (Player == null || Player.Scene != level)
                Player = level.Tracker.GetEntity<Player>();

            if (Wheel != null && Wheel.Scene != level) {
                Wheel.RemoveSelf();
                Wheel = null;
            }

            if (Player == null)
                goto End;

            if (Wheel == null)
                level.Add(Wheel = new GhostEmoteWheel(Player));

            if (!level.Paused && Settings.EmoteWheel && !Player.Dead) {
                Wheel.Shown = CelesteNetClientModule.Instance.JoystickEmoteWheel.Value.LengthSquared() >= 0.36f;
                int selected = Wheel.Selected;
                if (Wheel.Shown && selected != -1 && CelesteNetClientModule.Instance.ButtonEmoteSend.Pressed) {
                    Send(selected);
                }
            } else {
                Wheel.Shown = false;
                Wheel.Selected = -1;
            }

            if (!Context.Chat.Active) {
                if (MInput.Keyboard.Pressed(Keys.D1))
                    Send(0);
                else if (MInput.Keyboard.Pressed(Keys.D2))
                    Send(1);
                else if (MInput.Keyboard.Pressed(Keys.D3))
                    Send(2);
                else if (MInput.Keyboard.Pressed(Keys.D4))
                    Send(3);
                else if (MInput.Keyboard.Pressed(Keys.D5))
                    Send(4);
                else if (MInput.Keyboard.Pressed(Keys.D6))
                    Send(5);
                else if (MInput.Keyboard.Pressed(Keys.D7))
                    Send(6);
                else if (MInput.Keyboard.Pressed(Keys.D8))
                    Send(7);
                else if (MInput.Keyboard.Pressed(Keys.D9))
                    Send(8);
                else if (MInput.Keyboard.Pressed(Keys.D0))
                    Send(9);
            }

            End:
            if (Wheel?.Shown ?? false)
                Context.Main.StateUpdated |= Context.Main.ForceIdle.Add("EmoteWheel");
            else
                Context.Main.StateUpdated |= Context.Main.ForceIdle.Remove("EmoteWheel");
        }

        #region Hooks

        private void OnHeartGemCollect(On.Celeste.HeartGem.orig_Collect orig, HeartGem self, Player player) {
            orig(self, player);
            Wheel?.TimeRateSkip.Add("HeartGem");
        }

        private void OnHeartGemEndCutscene(On.Celeste.HeartGem.orig_EndCutscene orig, HeartGem self) {
            orig(self);
            Wheel?.TimeRateSkip.Remove("HeartGem");
        }

        private PlayerDeadBody OnPlayerDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
            PlayerDeadBody pdb = orig(self, direction, evenIfInvincible, registerDeathInStats);
            if (pdb != null && Wheel != null) {
                Wheel.TimeRateSkip.Add("PlayerDead");
                Wheel.Shown = false;
                Wheel.ForceSetTimeRate = true;
            }
            return pdb;
        }

        #endregion

    }
}
