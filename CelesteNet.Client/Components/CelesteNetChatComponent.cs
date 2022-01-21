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
    public class CelesteNetChatComponent : CelesteNetGameComponent {

        protected float _Time;

        public float Scale => Settings.UIScaleChat;
        protected int ScrolledFromIndex = 0;
        protected float ScrolledDistance = 0f;
        protected int skippedMsgCount = 0;

        public string PromptMessage = "";
        public Color PromptMessageColor = Color.White;

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

        protected Vector2 ScrollPromptSize = new Vector2(
                                GFX.Gui["controls/directions/0x-1"].Width + GFX.Gui["controls/keyboard/PageUp"].Width,
                                Math.Max(GFX.Gui["controls/directions/0x-1"].Height, GFX.Gui["controls/keyboard/PageUp"].Height)
                            );
        public float ScrollFade => (int) Settings.ChatScrollFading / 2f;

        public enum ChatScrollFade {
            None = 0,
            Fast = 1,
            Smooth = 2
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
        protected bool _CursorMoveFast = false;

        protected float _TimeSinceCursorMove = 0;
        protected float _CursorInitialMoveDelay = 0.4f;
        protected float _CursorMoveDelay = 0.1f;

        protected bool _SceneWasPaused;
        protected int _ConsumeInput;
        protected bool _Active;
        public bool Active {
            get => _Active;
            set {
                if (_Active == value)
                    return;
                ScrolledDistance = 0f;
                ScrolledFromIndex = 0;
                SetPromptMessage("");

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

            Persistent = true;
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
            lock (Log) {
                if (msg.Player?.ID == Client.PlayerInfo?.ID) {
                    foreach (DataChat pending in Pending.Values) {
                        Log.Remove(pending);
                        LogSpecial.Remove(pending);
                    }
                    Pending.Clear();
                }

                int index = Log.FindLastIndex(other => other.ID == msg.ID);
                if (index != -1) {
                    Log[index] = msg;
                } else {
                    Log.Add(msg);
                }

                if (msg.Color != Color.White) {
                    index = LogSpecial.FindLastIndex(other => other.ID == msg.ID);
                    if (index != -1) {
                        LogSpecial[index] = msg;
                    } else {
                        LogSpecial.Add(msg);
                    }
                }
            }
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            _Time += Engine.RawDeltaTime;
            _TimeSinceCursorMove += Engine.RawDeltaTime;

            bool isRebinding = Engine.Scene == null ||
                Engine.Scene.Entities.FindFirst<KeyboardConfigUI>() != null ||
                Engine.Scene.Entities.FindFirst<ButtonConfigUI>() != null;

            if (!(Engine.Scene?.Paused ?? true) || isRebinding) {
                string typing = Typing;
                Active = false;
                Typing = typing;
            }

            if (!Active && !isRebinding && Settings.ButtonChat.Button.Pressed) {
                Active = true;

            } else if (Active) {
                Engine.Commands.Open = false;

                ScrolledDistance = Math.Max(0f, ScrolledDistance + (MInput.Keyboard.CurrentState[Keys.PageUp] - MInput.Keyboard.CurrentState[Keys.PageDown]) * 2f * Settings.ChatScrollSpeed);
                if (ScrolledDistance < 10f) {
                    ScrolledFromIndex = Log.Count;
                }

                _ControlHeld = MInput.Keyboard.Check(Keys.LeftControl) || MInput.Keyboard.Check(Keys.RightControl);

                if (!MInput.Keyboard.Check(Keys.Left) && !MInput.Keyboard.Check(Keys.Right)) {
                    _CursorMoveFast = false;
                    _TimeSinceCursorMove = 0;
                }

                // boolean to determine if Left or Right were already held on previous frame
                bool _directionHeldLast = MInput.Keyboard.PreviousState[Keys.Left] == KeyState.Down
                                       || MInput.Keyboard.PreviousState[Keys.Right] == KeyState.Down;

                bool _cursorCanMove = true;
                // conditions for the cursor to be moving:
                // 1. Don't apply delays on first frame Left/Right is pressed
                if (_directionHeldLast) {
                    // 2. Delay time depends on whether this is the initial delay or subsequent "scrolling" left or right
                    _cursorCanMove = _TimeSinceCursorMove > (_CursorMoveFast ? _CursorMoveDelay : _CursorInitialMoveDelay);
                }

                if (MInput.Keyboard.Pressed(Keys.Enter)) {
                    if (!string.IsNullOrWhiteSpace(Typing))
                        Repeat.Insert(1, Typing);
                    Send(Typing);
                    Active = false;

                } else if (MInput.Keyboard.Pressed(Keys.Down) && RepeatIndex > 0) {
                    RepeatIndex--;

                } else if (MInput.Keyboard.Pressed(Keys.Up) && RepeatIndex < Repeat.Count - 1) {
                    RepeatIndex++;

                } else if (MInput.Keyboard.Check(Keys.Left) && _cursorCanMove && CursorIndex > 0) {
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
                    _TimeSinceCursorMove = 0;
                    _CursorMoveFast = _directionHeldLast;
                    _Time = 0;

                } else if (MInput.Keyboard.Check(Keys.Right) && _cursorCanMove && CursorIndex < Typing.Length) {
                    if (_ControlHeld) {
                        int nextWord = Typing.IndexOf(" ", _CursorIndex);
                        CursorIndex = (nextWord < 0) ? Typing.Length : nextWord + 1;
                    } else {
                        CursorIndex++;
                    }
                    _TimeSinceCursorMove = 0;
                    _CursorMoveFast = _directionHeldLast;
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

            } else if (c == (char) 127 && CursorIndex < Typing.Length) {
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

        public void SetPromptMessage(string msg, Color? color = null) {
            PromptMessage = msg;
            PromptMessageColor = color ?? Color.White;
        }

        protected override void Render(GameTime gameTime, bool toBuffer) {
            float scale = Scale;
            Vector2 fontScale = Vector2.One * scale;

            lock (Log) {
                List<DataChat> log = Mode switch {
                    ChatMode.Special => LogSpecial,
                    ChatMode.Off => Dummy<DataChat>.EmptyList,
                    _ => Log,
                };

                if (log.Count > 0) {
                    DateTime now = DateTime.UtcNow;

                    float y = UI_HEIGHT - 50f * scale;
                    if (Active)
                        y -= 105f * scale;

                    float scrollOffset = ScrolledDistance;
                    float logLength = Settings.ChatLogLength;
                    int renderedCount = 0;
                    skippedMsgCount = 0;
                    int count = ScrolledFromIndex > 0 ? ScrolledFromIndex : log.Count;
                    for (int i = 0; i < count; i++) {
                        DataChat msg = log[count - 1 - i];

                        float alpha = 1f;
                        float delta = (float) (now - msg.ReceivedDate).TotalSeconds;
                        if (!Active && delta > 3f)
                            alpha = 1f - Ease.CubeIn(delta - 3f);
                        if (alpha <= 0f)
                            continue;

                        string time = msg.Date.ToLocalTime().ToLongTimeString();

                        string text = msg.ToString(true, false);

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

                        float cutoff = 0f;
                        if (renderedCount == 0) {
                            if (scrollOffset <= height) {
                                y += scrollOffset;
                                cutoff = scrollOffset;
                                logLength += cutoff / height;
                            } else {
                                skippedMsgCount++;
                                scrollOffset -= height;
                                continue;
                            }
                        }
                        int msgExtraLines = Math.Max(0, text.Count(c => c == '\n') - 1 - (int) (cutoff / sizeText.Y));
                        renderedCount++;

                        y -= height;

                        // fade at the bottom
                        alpha -= ScrollFade * cutoff / height;

                        // fade at the top
                        if (renderedCount >= logLength)
                            alpha -= ScrollFade * Math.Max(0, renderedCount - logLength);
                        
                        logLength -= msgExtraLines * 0.75f * (cutoff > 0f ? 1f - cutoff / height : 1f);

                        Context.RenderHelper.Rect(25f * scale, y, size.X + 50f * scale, height - cutoff, Color.Black * 0.8f * alpha);
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

                        if (renderedCount >= logLength) {
                            break;
                        }
                    }

                    if (Active && renderedCount <= 1) {
                        ScrolledDistance -= scrollOffset;
                    }

                    if (Active) {
                        float x = 50f * scale;
                        y -= 2 * ScrollPromptSize.Y * scale;

                        Context.RenderHelper.Rect(x - 25f * scale, y, 50f * scale + ScrollPromptSize.X * scale, 2 * ScrollPromptSize.Y * scale, Color.Black * 0.8f);

                        GFX.Gui["controls/keyboard/PageUp"].Draw(
                            new(x, y),
                            Vector2.Zero,
                            MInput.Keyboard.CurrentState[Keys.PageUp] == KeyState.Down && renderedCount > 1 ? Color.Goldenrod : Color.White,
                            scale
                        );

                        GFX.Gui["controls/keyboard/PageDown"].Draw(
                            new(x, y + ScrollPromptSize.Y * scale),
                            Vector2.Zero,
                            MInput.Keyboard.CurrentState[Keys.PageDown] == KeyState.Down && ScrolledDistance > 0f ? Color.Goldenrod : Color.White,
                            scale
                        );

                        GFX.Gui["controls/directions/0x-1"].Draw(
                            new(x + GFX.Gui["controls/keyboard/PageUp"].Width * scale, y),
                            Vector2.Zero,
                            Color.White * (MInput.Keyboard.CurrentState[Keys.PageUp] == KeyState.Down && renderedCount > 1 ? 1f : .7f),
                            scale
                        );
                        GFX.Gui["controls/directions/0x1"].Draw(
                            new(x + GFX.Gui["controls/keyboard/PageDown"].Width * scale, y + ScrollPromptSize.Y * scale),
                            Vector2.Zero,
                            Color.White * (MInput.Keyboard.CurrentState[Keys.PageDown] == KeyState.Down && ScrolledDistance > 0f ? 1f : .7f),
                            scale
                        );
                    }
                }
            }

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

                if (ScrolledFromIndex > 0)
                    skippedMsgCount += Log.Count - ScrolledFromIndex;

                if (Typing.Length > 0 && skippedMsgCount > 0) {
                    CelesteNetClientFont.Draw(
                        $"({skippedMsgCount} newer message{(skippedMsgCount > 1 ? "s" : "")} off-screen!)",
                        new(200f * scale + CelesteNetClientFont.Measure(text).X * scale, UI_HEIGHT - 105f * scale),
                        Vector2.Zero,
                        fontScale,
                        Color.OrangeRed
                    );
                } else if (ScrolledFromIndex > 0 && ScrolledFromIndex < Log.Count) {
                    CelesteNetClientFont.Draw(
                        $"({Log.Count - ScrolledFromIndex} new message{(Log.Count - ScrolledFromIndex > 1 ? "s" : "")} since you scrolled up!)",
                        new(200f * scale + CelesteNetClientFont.Measure(text).X * scale, UI_HEIGHT - 105f * scale),
                        Vector2.Zero,
                        fontScale,
                        Color.GreenYellow
                    );
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
