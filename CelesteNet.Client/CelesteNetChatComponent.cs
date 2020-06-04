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
    public class CelesteNetChatComponent : CelesteNetGameComponent {

        protected Overlay _DummyOverlay = new Overlay();

        public List<DataChat> Log = new List<DataChat>();
        public string Typing = "";

        public List<string> Repeat = new List<string>() {
            ""
        };

        protected int _RepeatIndex = 0;
        public int RepeatIndex {
            get => _RepeatIndex;
            set {
                if (_RepeatIndex == value)
                    return;

                value = (value + Repeat.Count) % Repeat.Count;

                if (_RepeatIndex == 0 && value != 0)
                    Repeat[0] = Typing;
                Typing = Repeat[value];
                _RepeatIndex = value;
            }
        }

        protected bool _SceneWasPaused;
        protected int _ConsumeInput;
        protected bool _Active;
        public bool Active {
            get => _Active;
            set {
                if (_Active == value)
                    return;

                if (value) {
                    _SceneWasPaused = Engine.Scene.Paused;
                    Engine.Scene.Paused = true;
                    // If we're in a level, add a dummy overlay to prevent the pause menu from handling input.
                    if (Engine.Scene is Level level)
                        level.Overlay = _DummyOverlay;

                    _RepeatIndex = 0;

                } else {
                    Typing = "";
                    Engine.Scene.Paused = _SceneWasPaused;
                    _ConsumeInput = 2;
                    if (Engine.Scene is Level level && level.Overlay == _DummyOverlay)
                        level.Overlay = null;
                }

                _Active = value;
            }
        }

        public CelesteNetChatComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10001;
        }

        public override void Update(GameTime gameTime) {
            if (!(Engine.Scene?.Paused ?? true)) {
                string typing = Typing;
                Active = false;
                Typing = typing;
            }

            if (!Active && CelesteNetClientModule.Settings.ButtonChat.Button.Pressed) {
                Active = true;

            } else if (Active) {
                Engine.Commands.Open = false;

                if (MInput.Keyboard.Pressed(Keys.Enter)) {
                    // AAAAAAAAAAAAAAAAA
                    Active = false;

                } else if (MInput.Keyboard.Pressed(Keys.Down) && RepeatIndex > 0) {
                    RepeatIndex--;
                } else if (MInput.Keyboard.Pressed(Keys.Up) && RepeatIndex < Repeat.Count - 1) {
                    RepeatIndex++;

                } else if (Input.ESC.Pressed || Input.Pause.Pressed) {
                    Active = false;
                }
            }

            // Prevent the menus from reacting to player input after exiting the chat.
            if (_ConsumeInput > 0) {
                Input.MenuConfirm.ConsumeBuffer();
                Input.MenuConfirm.ConsumePress();
                Input.ESC.ConsumeBuffer();
                Input.ESC.ConsumePress();
                Input.Pause.ConsumeBuffer();
                Input.Pause.ConsumePress();
                _ConsumeInput--;
            }

        }

        public override void Draw(GameTime gameTime) {
            if (_Active)
                base.Draw(gameTime);
        }

        protected override void Render(GameTime gameTime, bool toBuffer) {

        }

    }
}
