﻿using Celeste.Mod.CelesteNet.Client.Entities;
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
    public class CelesteNetChatComponent : CelesteNetGameComponent {

        protected float _Time;

        public float Scale => Settings.UIScale;

        public float? RenderPositionY { get; private set; } = null;

        protected Overlay _DummyOverlay = new PauseUpdateOverlay();

        public List<DataChat> Log = new();
        public List<DataChat> LogSpecial = new();
        public Dictionary<string, DataChat> Pending = new();
        public string Typing = "";

        public ChatMode Mode => Active ? ChatMode.All : Settings.ShowNewMessages;

        public enum ChatMode {
            All,
            Special,
            Off
        }

        public List<string> Repeat = new() {
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
                _CursorIndex = Typing.Length;
            }
        }

        protected int _CursorIndex = 0;
        public int CursorIndex {
            get => _CursorIndex;
            set {
                if (value < 0)
                    value = 0;

                // This deliberately exceeds the Typing string's indices since the cursor
                // 1. ... at index 0 is before the first char,
                // 2. ... at Typing.Length-1 is before the last char,
                // and at Typing.Length is _after_ the last char.
                if (value > Typing.Length)
                    value = Typing.Length;

                _CursorIndex = value;
            }
        }

        protected bool _ControlHeld = false;

        protected InputRepeatDelay LeftRightRepeatDelay = new(Keys.Left, Keys.Right);
        protected InputRepeatDelay DeleteRepeatDelay = new(Keys.Delete);

        public class InputRepeatDelay {
            /* A class to implement a key input delay that when held
             * - allows the action to perform right away
             * - then has an inital delay before triggering again
             * - after that uses a faster moving delay to trigger
             * e.g. like when holding down Left/Right Arrow Key in a text editor.
             */

            public readonly Keys InputA;
            public readonly Keys InputB;

            public readonly float InitialDelay;
            public readonly float MoveDelay;

            protected bool IsDownA => MInput.Keyboard.Check(InputA);
            protected bool IsDownB => MInput.Keyboard.Check(InputB);
            protected bool WasDownA => MInput.Keyboard.PreviousState[InputA] == KeyState.Down;
            protected bool WasDownB => MInput.Keyboard.PreviousState[InputB] == KeyState.Down;

            public bool CanMove => timeSinceMoved > (moveFast ? MoveDelay : InitialDelay) || (!WasDownA && !WasDownB);

            private bool moveFast = false;
            private float timeSinceMoved = 0;

            public InputRepeatDelay(Keys A, Keys B = Keys.None, float initialDelay = 0.3f, float moveDelay = 0.05f) {
                // can be used with a single key, where InputB gets set to A also
                InputA = A;
                InputB = (B != Keys.None) ? B : A;
                InitialDelay = initialDelay;
                MoveDelay = moveDelay;
            }

            public void Update(float time) {
                timeSinceMoved += time;

                if (!IsDownA && !IsDownB) {
                    // always reset so that first press has no delays
                    timeSinceMoved = 0;
                }
            }

            public bool Check(Keys K) {
                if (K != InputA && K != InputB)
                    return false;
                return MInput.Keyboard.Check(K) && CanMove;
            }

            public void Triggered() {
                timeSinceMoved = 0;
                // this is where moveFast gets set, so that MoveDelay only gets used after first trigger had InitialDelay
                moveFast = WasDownA || WasDownB;
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
                    _Time = 0;
                    TextInput.OnInput += OnTextInput;

                } else {
                    Typing = "";
                    CursorIndex = 0;
                    Engine.Scene.Paused = _SceneWasPaused;
                    _ConsumeInput = 2;
                    if (Engine.Scene is Level level && level.Overlay == _DummyOverlay)
                        level.Overlay = null;
                    TextInput.OnInput -= OnTextInput;
                }

                _Active = value;
            }
        }

        public CelesteNetChatComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10100;
        }

        public void Send(string text) {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text))
                return;

            lock (Log) {
                if (Pending.ContainsKey(text))
                    return;
                DataChat msg = new() {
                    Player = Client.PlayerInfo,
                    Text = text
                };
                Pending[text] = msg;
                Log.Add(msg);
                LogSpecial.Add(msg);
                Client.Send(msg);
            }
        }

        public void Handle(CelesteNetConnection con, DataChat msg) {
            if (Client == null)
                return;

            lock (Log) {
                if (msg.Player?.ID == Client.PlayerInfo?.ID) {
                    foreach (DataChat pending in Pending.Values) {
                        Log.Remove(pending);
                        LogSpecial.Remove(pending);
                    }
                    Pending.Clear();
                }

                int index = Log.FindLastIndex(other => other.ID == msg.ID);
                if (index == -1) {
                    index = Log.FindLastIndex(other => other.ID < msg.ID);
                    if (index == -1)
                        index = Log.Count - 1;
                    Log.Insert(index + 1, msg);
                } else if (Log[index].Version <= msg.Version) {
                    Log[index] = msg;
                }
                if (msg.Color != Color.White) {
                    index = LogSpecial.FindLastIndex(other => other.ID == msg.ID);
                    if (index == -1) {
                        index = LogSpecial.FindLastIndex(other => other.ID < msg.ID);
                        if (index == -1)
                            index = LogSpecial.Count - 1;
                        LogSpecial.Insert(index + 1, msg);
                    } else if (LogSpecial[index].Version <= msg.Version) {
                        LogSpecial[index] = msg;
                    }
                }
            }
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (Client == null) {
                Active = false;
                return;
            }

            _Time += Engine.RawDeltaTime;

            Overworld overworld = Engine.Scene as Overworld;
            bool isRebinding = Engine.Scene == null ||
                Engine.Scene.Entities.FindFirst<KeyboardConfigUI>() != null ||
                Engine.Scene.Entities.FindFirst<ButtonConfigUI>() != null ||
                ((overworld?.Current ?? overworld?.Next) is OuiFileNaming naming && naming.UseKeyboardInput) ||
                ((overworld?.Current ?? overworld?.Next) is UI.OuiModOptionString stringInput && stringInput.UseKeyboardInput);

            if (!(Engine.Scene?.Paused ?? true) || isRebinding) {
                string typing = Typing;
                Active = false;
                Typing = typing;
            }

            if (!Active && !isRebinding && Settings.ButtonChat.Button.Pressed) {
                Active = true;

            } else if (Active) {
                Engine.Commands.Open = false;

                _ControlHeld = MInput.Keyboard.Check(Keys.LeftControl) || MInput.Keyboard.Check(Keys.RightControl);

                LeftRightRepeatDelay.Update(Engine.RawDeltaTime);
                DeleteRepeatDelay.Update(Engine.RawDeltaTime);

                if (MInput.Keyboard.Pressed(Keys.Enter)) {
                    if (!string.IsNullOrWhiteSpace(Typing))
                        Repeat.Insert(1, Typing);
                    Send(Typing);
                    Active = false;

                } else if (MInput.Keyboard.Pressed(Keys.Down) && RepeatIndex > 0) {
                    RepeatIndex--;

                } else if (MInput.Keyboard.Pressed(Keys.Up) && RepeatIndex < Repeat.Count - 1) {
                    RepeatIndex++;

                } else if (LeftRightRepeatDelay.Check(Keys.Left) && CursorIndex > 0) {
                    if (_ControlHeld) {
                        // skip over space right before the cursor, if there is one
                        if (Typing[_CursorIndex - 1] == ' ')
                            CursorIndex--;
                        if (CursorIndex > 0) {
                            int prevWord = Typing.LastIndexOf(" ", _CursorIndex - 1);
                            CursorIndex = (prevWord < 0) ? 0 : prevWord + 1;
                        }
                    } else {
                        CursorIndex--;
                    }
                    LeftRightRepeatDelay.Triggered();
                    _Time = 0;

                } else if (LeftRightRepeatDelay.Check(Keys.Right) && CursorIndex < Typing.Length) {
                    if (_ControlHeld) {
                        int nextWord = Typing.IndexOf(" ", _CursorIndex);
                        CursorIndex = (nextWord < 0) ? Typing.Length : nextWord + 1;
                    } else {
                        CursorIndex++;
                    }
                    LeftRightRepeatDelay.Triggered();
                    _Time = 0;
                } else if (DeleteRepeatDelay.Check(Keys.Delete) && CursorIndex < Typing.Length) {
                    // Delete - remove character after cursor.
                    if (_ControlHeld && Typing[_CursorIndex] != ' ') {
                        int nextWord = Typing.IndexOf(" ", _CursorIndex);
                        // if control is held and a space is found, remove from cursor to space
                        if (nextWord >= 0) {
                            // include the found space in removal
                            nextWord++;
                            Typing = Typing.Remove(_CursorIndex, nextWord - _CursorIndex);
                        } else {
                            // otherwise remove everything after cursor
                            Typing = Typing.Substring(0, _CursorIndex);
                        }
                    } else {
                        // just remove single char
                        Typing = Typing.Remove(_CursorIndex, 1);
                    }
                    DeleteRepeatDelay.Triggered();
                    _RepeatIndex = 0;
                    _Time = 0;

                } else if (MInput.Keyboard.Pressed(Keys.Home)) {
                    CursorIndex = 0;

                } else if (MInput.Keyboard.Pressed(Keys.End)) {
                    CursorIndex = Typing.Length;

                } else if (Input.ESC.Pressed) {
                    Active = false;
                }
            }

            // Prevent menus from reacting to player input after exiting chat.
            if (_ConsumeInput > 0) {
                Input.MenuConfirm.ConsumeBuffer();
                Input.MenuConfirm.ConsumePress();
                Input.ESC.ConsumeBuffer();
                Input.ESC.ConsumePress();
                Input.Pause.ConsumeBuffer();
                Input.Pause.ConsumePress();
                Input.QuickRestart.ConsumeBuffer();
                Input.QuickRestart.ConsumePress();
                _ConsumeInput--;
            }

        }

        public void OnTextInput(char c) {
            if (!Active)
                return;

            if (c == (char) 13) {
                // Enter - send.
                // Handled in Update.

            } else if (c == (char) 8 && _CursorIndex > 0) {
                // Backspace - trim.
                if (Typing.Length > 0) {
                    int trim = 1;

                    // extra CursorIndex check since at index=1 using trim=1 is fine
                    if (_ControlHeld && _CursorIndex > 1) {
                        // adjust Ctrl+Backspace for having a space right before cursor
                        int _adjustedCursor = CursorIndex;
                        if (Typing[_CursorIndex - 1] == ' ')
                            _adjustedCursor--;
                        int prevWord = Typing.LastIndexOf(" ", _adjustedCursor - 1);
                        // if control is held and a space is found, trim from cursor back to space
                        if (prevWord >= 0)
                            trim = _adjustedCursor - prevWord;
                        // otherwise trim whole input back from cursor as it is one word
                        else
                            trim = _adjustedCursor;
                    }
                    // remove <trim> amount of characters before cursor
                    Typing = Typing.Remove(_CursorIndex - trim, trim);
                    _CursorIndex -= trim;
                }
                _RepeatIndex = 0;
                _Time = 0;

            } else if (!char.IsControl(c)) {
                if (CursorIndex == Typing.Length) {
                    // Any other character - append.
                    Typing += c;
                } else {
                    // insert into string if cursor is not at the end
                    Typing = Typing.Insert(_CursorIndex, c.ToString());
                }
                _CursorIndex++;
                _RepeatIndex = 0;
                _Time = 0;
            }
        }

        protected override void Render(GameTime gameTime, bool toBuffer) {
            float scale = Scale;
            Vector2 fontScale = Vector2.One * scale;

            if (Active) {
                Context.RenderHelper.Rect(25f * scale, UI_HEIGHT - 125f * scale, UI_WIDTH - 50f * scale, 100f * scale, Color.Black * 0.8f);

                CelesteNetClientFont.Draw(
                    ">",
                    new(50f * scale, UI_HEIGHT - 105f * scale),
                    Vector2.Zero,
                    fontScale * new Vector2(0.5f, 1f),
                    Color.White * 0.5f
                );
                float offs = CelesteNetClientFont.Measure(">").X * scale;

                string text = Typing;
                CelesteNetClientFont.Draw(
                    text,
                    new(50f * scale + offs, UI_HEIGHT - 105f * scale),
                    Vector2.Zero,
                    fontScale,
                    Color.White
                );

                if (!Calc.BetweenInterval(_Time, 0.5f)) {

                    if (CursorIndex == Typing.Length) {
                        offs += CelesteNetClientFont.Measure(text).X * scale;
                        CelesteNetClientFont.Draw(
                            "_",
                            new(50f * scale + offs, UI_HEIGHT - 105f * scale),
                            Vector2.Zero,
                            fontScale,
                            Color.White * 0.5f
                        );
                    } else {
                        // draw cursor at correct location, but move back half a "." width to not overlap following char
                        offs += CelesteNetClientFont.Measure(Typing.Substring(0, CursorIndex)).X * scale;
                        offs -= CelesteNetClientFont.Measure(".").X / 2f * scale;

                        CelesteNetClientFont.Draw(
                               "|",
                               new(50f * scale + offs, UI_HEIGHT - 110f * scale),
                               Vector2.Zero,
                               fontScale * new Vector2(.5f, 1.2f),
                               Color.White * 0.6f
                           );
                    }
                }
            }

            RenderPositionY = null;

            lock (Log) {
                List<DataChat> log = Mode switch {
                    ChatMode.Special => LogSpecial,
                    ChatMode.Off => Dummy<DataChat>.EmptyList,
                    _ => Log,
                };

                int count = log.Count;
                if (count > 0) {
                    DateTime now = DateTime.UtcNow;

                    float y = UI_HEIGHT - 50f * scale;
                    if (Active)
                        y -= 105f * scale;

                    float logLength = Settings.ChatLogLength;
                    for (int i = 0; i < count && i < logLength; i++) {
                        DataChat msg = log[count - 1 - i];

                        float alpha = 1f;
                        float delta = (float) (now - msg.ReceivedDate).TotalSeconds;
                        if (!Active && delta > 3f)
                            alpha = 1f - Ease.CubeIn(delta - 3f);
                        if (alpha <= 0f)
                            continue;

                        string time = msg.Date.ToLocalTime().ToLongTimeString();

                        string text = msg.ToString(true, false);
                        logLength -= Math.Max(0, text.Count(c => c == '\n') - 1) * 0.75f;

                        int lineScaleTry = 0;
                        float lineScale = scale;
                        RetryLineScale:
                        Vector2 lineFontScale = Vector2.One * lineScale;

                        Vector2 sizeTime = CelesteNetClientFontMono.Measure(time) * lineFontScale;
                        Vector2 sizeText = CelesteNetClientFont.Measure(text) * lineFontScale;
                        Vector2 size = new(sizeTime.X + 25f * scale + sizeText.X, Math.Max(sizeTime.Y - 5f * scale, sizeText.Y));

                        if ((size.X + 100f * scale) > UI_WIDTH && lineScaleTry < 4) {
                            lineScaleTry++;
                            lineScale -= scale * 0.1f;
                            goto RetryLineScale;
                        }

                        float height = 50f * scale + size.Y;

                        y -= height;

                        Context.RenderHelper.Rect(25f * scale, y, size.X + 50f * scale, height, Color.Black * 0.8f * alpha);
                        CelesteNetClientFontMono.Draw(
                            time,
                            new(50f * scale, y + 20f * scale),
                            Vector2.Zero,
                            lineFontScale,
                            msg.Color * alpha * (msg.ID == uint.MaxValue ? 0.8f : 1f)
                        );
                        CelesteNetClientFont.Draw(
                            text,
                            new(75f * scale + sizeTime.X, y + 25f * scale),
                            Vector2.Zero,
                            lineFontScale,
                            msg.Color * alpha * (msg.ID == uint.MaxValue ? 0.8f : 1f)
                        );
                    }

                    RenderPositionY = y;
                }
            }
        }

        protected override void Dispose(bool disposing) {
            if (Active)
                Active = false;

            base.Dispose(disposing);
        }

    }
}
