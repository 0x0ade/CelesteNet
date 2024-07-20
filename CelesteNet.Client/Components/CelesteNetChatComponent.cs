using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetChatComponent : CelesteNetGameComponent {

        protected float _Time;

        public float Scale => Settings.UIScaleChat;
        public float CustomAlpha => Settings.UICustomize.ChatOpacity / 20f;

        protected int ScrolledFromIndex = 0;
        protected float ScrolledDistance = 0f;
        protected int skippedMsgCount = 0;

        public int InputScrollUpState => Settings.ButtonChatScrollUp.Check ? 1 : 0;
        public int InputScrollDownState => Settings.ButtonChatScrollDown.Check ? 1 : 0;

        public string PromptMessage = "";
        public Color PromptMessageColor = Color.White;

        // FIXME aaaaaaAAAAAAAAAAAAA i don't like this is hardcoded kdfj;askldjfkls
        public Color WhisperChatColor = Calc.HexToColor("#888888");
        public Color PublicChatColor = Color.White;

        // will get filled out once we join the server
        protected string CurrentChannelName = "main";

        public float? RenderPositionY { get; private set; } = null;

        protected Overlay _DummyOverlay = new PauseUpdateOverlay();

        public List<DataChat> Log = new();
        public List<DataChat> LogSpecial = new();
        public Dictionary<string, DataChat> Pending = new();
        public string Typing = "";

        public List<CommandInfo> CommandList = new();
        public Dictionary<string, string> CommandAliasLookup = new();

        public ChatMode Mode => Active ? ChatMode.All : Settings.ChatUI.ShowNewMessages;

        // used for hiding your own channel name
        // match anything other than main

        // check for /cc
        protected Regex ChannelTagRegex = new Regex(@"channel (?!main)\S+", RegexOptions.Compiled);
        // check for /join|channel (may contain spaces in name)
        protected Regex ChannelCMDRegex = new Regex(@"^\/(join|channel) ((?!main).+)", RegexOptions.Compiled);
        // check for /join|channel response
        protected Regex ChannelResponseRegex = new Regex(@"^(Moved to|Already in) (?!main)\S+", RegexOptions.Compiled);
        // check for /join|channel list
        protected Regex ChannelListHeaderRegex = new Regex(@"^You're in (?!main)\S+", RegexOptions.Compiled);

        public enum ChatMode {
            All,
            Special,
            Off
        }

        public MTexture InputScrollUpIcon;
        public MTexture InputScrollDownIcon;
        public MTexture ArrowUpIcon => GFX.Gui["controls/directions/0x-1"];
        public MTexture ArrowDownIcon => GFX.Gui["controls/directions/0x1"];
        private bool activeController = false;


        protected Vector2 ScrollPromptSize = new Vector2();
        public float ScrollFade => (int) Settings.ChatUI.ChatScrollFading / 2f;

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
                CompletionHidden = CompletionHiddenBy.RepeatIndex;
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
                if (CompletionHidden != CompletionHiddenBy.ChatClosePressed)
                    CompletionHidden = CompletionHiddenBy.None;
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

            // this setter does important stuff like unpause the game, remove dummy overlay, remove OnInput
            // and should ALWAYS be run with value = false when closing chat or disposing this component...
            set {
                // Override set value to false when not fully connected.
                var setToActive = value;
                if (Client == null || !Client.IsReady) {
                    setToActive = false;
                }

                ScrolledDistance = 0f;
                ScrolledFromIndex = 0;
                SetPromptMessage(PromptMessageTypes.None);

                // --- things that can be done always ---
                if (setToActive) {
                    _RepeatIndex = 0;
                    _Time = 0;
                    CompletionHidden = CompletionHiddenBy.None;

                    UpdateScrollPromptControls();
                } else {
                    Typing = "";
                    CursorIndex = 0;
                    UpdateCompletion(CompletionType.None);
                }

                // important to e.g. not overwrite _SceneWasPaused with our own value on repeated calls
                if (setToActive == _Active)
                    return;

                // --- things that should only happen on actual state transition of this property! ---
                if (setToActive) {
                    _SceneWasPaused = Engine.Scene.Paused;
                    Engine.Scene.Paused = true;

                    TextInput.OnInput += OnTextInput;

                    RefreshButtonsToSuppress();
                } else {
                    Engine.Scene.Paused = _SceneWasPaused;

                    TextInput.OnInput -= OnTextInput;

                    _ConsumeInput = 2;
                }

                // deal with in-game input-eating overlay
                if (Engine.Scene is Level level) {
                    // If we're in a level, add a dummy overlay to prevent the pause menu from handling input.
                    if (setToActive) {
                        level.Overlay = _DummyOverlay;
                    } else if (level.Overlay == _DummyOverlay) {
                        level.Overlay = null;
                    }
                }

                _Active = setToActive;
            }
        }

        protected List<string> Completion = new();

        public string CompletionPartial { get; private set; } = "";
        private int _CompletionSelected = -1;
        public int CompletionSelected { 
            get => _CompletionSelected; 
            private set {
                if (value == _CompletionSelected)
                    return;

                if (value < -1)
                    value = Completion.Count - 1;

                if (value >= Completion.Count)
                    value = -1;

                _CompletionSelected = value;
            }
        }
        protected CompletionType CompletionArgType;
        protected Atlas CompletionEmoteAtlas;
        private PromptMessageTypes PromptMessageType;

        private CompletionHiddenBy CompletionHidden = CompletionHiddenBy.None;

        public enum PromptMessageTypes {
            None = 0,
            Scroll,
            Info
        }

        public enum CompletionHiddenBy {
            None = 0,
            RepeatIndex,
            ChatClosePressed
        }

        protected List<VirtualButton> ButtonsToSuppress;

        public CelesteNetChatComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10100;

            RefreshButtonsToSuppress();
        }

        public void RefreshButtonsToSuppress() {
            // seems like we would need to do this after every time someone rebinds something,
            // because these stop working then? So just rebuild this every time chat opens...
            ButtonsToSuppress = new() {
                Input.ESC,
                Input.QuickRestart,

                // apparently some people put Dash on their Enter key
                // instead of just doing Dash i'm gonna put other binds too in case

                Input.Grab,
                Input.Jump,
                Input.Dash,
                Input.CrouchDash,

                Input.Talk,
                Input.Pause,
                Input.QuickRestart,

                Input.MenuConfirm,
                Input.MenuCancel
            };
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
                Client?.Send(msg);
            }
        }

        public void Handle(CelesteNetConnection con, DataChannelList channelList) {
            if (Client == null)
                return;

            // stolen homework from CelesteNetPlayerListComponent
            string tmp = channelList.List.FirstOrDefault(channel => channel.Players.Contains(Client.PlayerInfo.ID))?.Name;
            if (tmp == null) return;
            CurrentChannelName = tmp;
        }

        public void Handle(CelesteNetConnection con, DataChat msg) {
            if (Client == null)
                return;

            if (Settings.PlayerListUI.HideOwnChannelName) {
                // don't get too eager, only replace text in ACK'd commands and server responses
                if (msg.Color != PublicChatColor && msg.Color != WhisperChatColor) {
                    Match cmdMatch = ChannelCMDRegex.Match(msg.Text);
                    if (cmdMatch.Success) {
                        string channelOrPage = cmdMatch.Groups[2].Value;

                        // hide if the argument is not a parsable int
                        // a number > int.MaxValue is treated as a channel name
                        // main already excluded in regex
                        if (!int.TryParse(channelOrPage, out _))
                            msg.Text = ChannelCMDRegex.Replace(msg.Text, "/$1 <hidden>");
                    }

                    msg.Text = ChannelResponseRegex.Replace(msg.Text, "$1 <hidden>");
                    msg.Text = ChannelListHeaderRegex.Replace(msg.Text, "You're in <hidden>");
                    if (CurrentChannelName != "main")
                        msg.Text = Regex.Replace(msg.Text, $@"\b{CurrentChannelName} - (\d+) players\b", "<hidden> - $1 players");
                }
                msg.Tag = ChannelTagRegex.Replace(msg.Tag, "channel");
            }

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
                if (msg.Color != PublicChatColor) {
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

        public void Handle(CelesteNetConnection con, DataCommandList commands) {
            CommandList.Clear();
            CommandAliasLookup.Clear();
            foreach (CommandInfo cmd in commands.List) {
                Logger.Log(LogLevel.INF, "chat", $"Learned about server command: {cmd.ID}{(!cmd.AliasTo.IsNullOrEmpty() ? $" (alias of {cmd.AliasTo})" : "")} ({cmd.FirstArg})");

                if (!cmd.AliasTo.IsNullOrEmpty())
                    CommandAliasLookup[cmd.AliasTo] = cmd.ID;

                CommandList.Add(cmd);
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
            bool isOtherInputFocused = Engine.Scene == null ||
                Engine.Scene.Entities.FindFirst<KeyboardConfigUI>() != null ||
                Engine.Scene.Entities.FindFirst<ButtonConfigUI>() != null ||
                ((overworld?.Current ?? overworld?.Next) is OuiFileNaming naming && naming.UseKeyboardInput) ||
                ((overworld?.Current ?? overworld?.Next) is UI.OuiModOptionString stringInput && stringInput.UseKeyboardInput) ||
                Engine.Scene.Entities.FindAll<TextMenu>().Exists(m => m.Items.Find(item => item is TextMenuExt.Modal m && m.Visible) != null);
            // on the above I tried looking for "TextMenuExt.TextBox tb && tb.Typing" but somehow the TextBox isn't in the TextMenu?...
            // but the Modal's Added() should give assign the TextBox the same Container and call Added() on it, and it should be in Items...

            if (!(Engine.Scene?.Paused ?? true) || isOtherInputFocused) {
                string typing = Typing;
                Active = false;
                Typing = typing;
            }

            if (!Active && !isOtherInputFocused && Settings.ButtonChat.Button.Pressed) {
                Active = true;

            } else if (Active) {
                Engine.Commands.Open = false;

                ScrolledDistance = Math.Max(0f, ScrolledDistance + (InputScrollUpState - InputScrollDownState) * 2f * Settings.ChatUI.ChatScrollSpeed);
                if (ScrolledDistance < 10f) {
                    ScrolledFromIndex = Log.Count;
                }

                _ControlHeld = MInput.Keyboard.Check(Keys.LeftControl) || MInput.Keyboard.Check(Keys.RightControl);

                LeftRightRepeatDelay.Update(Engine.RawDeltaTime);
                DeleteRepeatDelay.Update(Engine.RawDeltaTime);

                if (Settings.ButtonChatSend.Pressed) {
                    if (!string.IsNullOrWhiteSpace(Typing))
                        Repeat.Insert(1, Typing);
                    Send(Typing);
                    Active = false;

                } else if (MInput.Keyboard.Pressed(Keys.Down)) {
                    if (Completion.Count > 0) {
                        CompletionSelected--;
                    } else if (RepeatIndex > 0) {
                            RepeatIndex--;
                    }
                } else if (MInput.Keyboard.Pressed(Keys.Up)) {
                    if (Completion.Count > 0) {
                        CompletionSelected++;
                    } else if (RepeatIndex < Repeat.Count - 1) {
                        RepeatIndex++;
                    }
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

                } else if (Settings.ButtonChatClose.Released) {
                    if (!Settings.ChatUI.ChatCloseCancelsSuggestions || Completion.Count == 0)
                        Active = false;
                    CompletionHidden = CompletionHiddenBy.ChatClosePressed;
                }

                if (Active) {
                    if (CompletionHidden != CompletionHiddenBy.None) {
                        UpdateCompletion(CompletionType.None);
                    } else {
                        int spaceBeforeCursor = -1;
                        string completable = "";
                        if (_CursorIndex > 0) {
                            spaceBeforeCursor = Typing.LastIndexOf(" ", _CursorIndex - 1) + 1;
                            if (spaceBeforeCursor < _CursorIndex) {
                                completable = Typing.Substring(0, _CursorIndex).Substring(spaceBeforeCursor);
                            }
                        }

                        if (completable.IsNullOrEmpty()) {
                            UpdateCompletion(CompletionType.None);
                        } else {
                            // completions for commands or their first parameter
                            if (Typing.StartsWith("/")) {
                                int firstSpace = Typing.IndexOf(" ");
                                CommandInfo cmd = firstSpace == -1 ? null : CommandList.FirstOrDefault(c => c.ID == Typing.Substring(1, firstSpace - 1));

                                if (Typing.Substring(0, _CursorIndex).Equals(completable)) {
                                    UpdateCompletion(CompletionType.Command, completable.Substring(1).ToLowerInvariant());
                                } else if (cmd != null) {
                                    if (Typing.Substring(0, spaceBeforeCursor).Count(c => c == ' ') == 1) {
                                        if (cmd.FirstArg != CompletionType.None) {
                                            UpdateCompletion(cmd.FirstArg, completable);
                                        }
                                    } else if (cmd.FirstArg == CompletionType.Emote) {
                                        UpdateCompletion(CompletionType.Emote, Typing.Substring(0, _CursorIndex).Substring(firstSpace + 1));
                                    }
                                }
                            }

                            if (completable.StartsWith(":") && completable.IndexOf(':', 1) == -1
                                && (CompletionArgType == CompletionType.None
                                || CompletionArgType == CompletionType.Emoji
                                || (CompletionArgType == CompletionType.Emote && Completion.Count == 0))) {
                                // purely client-side completions for chat-emotes
                                UpdateCompletion(CompletionType.Emoji, completable.Substring(1));
                            }
                        }
                    }
                }
            }

            // Prevent binds from reacting to player input after exiting chat.
            if (_ConsumeInput > 0) {
                foreach (VirtualButton button in ButtonsToSuppress) {
                    button.ConsumeBuffer();
                    button.ConsumePress();
                }

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
                if (CompletionHidden != CompletionHiddenBy.ChatClosePressed)
                    CompletionHidden = CompletionHiddenBy.None;
            } else if (c == (char) 9) {
                // Tab - completion
                string accepted = "";
                if (Completion.Count == 1) {
                    accepted = Completion[0];
                } else if (Completion.Count > 1 && CompletionSelected >= 0 && CompletionSelected < Completion.Count) {
                    accepted = Completion[CompletionSelected];
                }

                if (!accepted.IsNullOrEmpty()) {
                    // remove the thing being completed, since we're inserting the accepted one
                    // and if "Name" matches for "na" we want to end up with "Name", not "name".
                    _CursorIndex -= CompletionPartial.Length;
                    Typing = Typing.Remove(_CursorIndex, CompletionPartial.Length);

                    if (CompletionArgType == CompletionType.Command) {
                        string aliased = CommandList.Where(cmd => cmd.AliasTo == accepted).Select(c => c.ID).FirstOrDefault();
                        if (!aliased.IsNullOrEmpty())
                            accepted = aliased;
                    }

                    // This "cursor adjustment" stuff is so that when you have typed
                    // e.g. ":glad|: hi" (where | is the cursor position) my assumption is
                    // that the cursor should just move to before the "hi" instead of inserting
                    // a duplicate ": " to complete the emote.
                    int adjustedCursor = CursorIndex;

                    if (CompletionArgType == CompletionType.Emoji) {
                        if (CursorIndex == Typing.Length || Typing[_CursorIndex] != ':') {
                            accepted += ':';
                        } else if (Typing[_CursorIndex] == ':') {
                            adjustedCursor += 1;
                        }
                    }

                    if (CompletionArgType != CompletionType.Emote || !accepted.EndsWith("/")) {
                        if (adjustedCursor == Typing.Length || Typing[adjustedCursor] != ' ')
                            accepted += ' ';
                        else if (Typing[adjustedCursor] == ' ')
                            adjustedCursor += 1;
                    }

                    if (CursorIndex == Typing.Length) {
                        Typing += accepted;
                        CursorIndex += accepted.Length;
                    } else {
                        // insert into string if cursor is not at the end
                        Typing = Typing.Insert(_CursorIndex, accepted);
                        CursorIndex = adjustedCursor + accepted.Length;
                    }

                    UpdateCompletion(CompletionType.None);
                }
                CompletionHidden = CompletionHiddenBy.None;
            } else if (!char.IsControl(c) && CelesteNetUtils.EnglishFontCharsSet.Contains(c)) {
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
                if (CompletionHidden != CompletionHiddenBy.ChatClosePressed)
                    CompletionHidden = CompletionHiddenBy.None;

                if (c == ' ') {
                    UpdateCompletion(CompletionType.None);
                    CompletionHidden = CompletionHiddenBy.None;
                }
            }
        }

        public void SetPromptMessage(PromptMessageTypes type, string msg = "", Color? color = null) {
            PromptMessageType = type;
            PromptMessage = msg;
            PromptMessageColor = color ?? Color.White;
        }

        public void UpdateCompletion(CompletionType type, string partial = "") {
            if (partial == CompletionPartial && type == CompletionArgType && Completion.Count == 0)
                return;

            partial = partial.Trim();
            CompletionPartial = partial;
            CompletionArgType = type;

            Completion.Clear();
            if (type == CompletionType.None) {
                CompletionPartial = "";
                CompletionSelected = -1;
                CompletionEmoteAtlas = null;
                return;
            }

            switch (type) {
                case CompletionType.Command:
                    if (string.IsNullOrEmpty(partial)) {
                        Completion = CommandList.Where(cmd => cmd.AliasTo == "").Select(cmd => cmd.ID).ToList();
                    }
                    else {
                        // I thought I could be smart and make the Where() only do the StartsWith() and then have a ternary (?:) in the Select() with the AliasTo condition
                        // but then I get duplicates from the same command names being one's ID and another's AliasTo, so this is probably still the best I got
                        IEnumerable<string> commands = CommandList.Where(cmd => cmd.ID.StartsWith(partial) && cmd.AliasTo == "").Select(cmd => cmd.ID);
                        IEnumerable<string> aliased = CommandList.Where(cmd => cmd.ID.StartsWith(partial) && cmd.AliasTo != "").Select(cmd => cmd.AliasTo);
                        Completion = commands.Union(aliased).ToList();
                    }

                    break;
                case CompletionType.Player:
                    DataPlayerInfo[] all = Client.Data.GetRefs<DataPlayerInfo>();

                    Completion = all.Select(p => p.FullName).Where(name => name.StartsWith(partial, StringComparison.InvariantCultureIgnoreCase)).ToList();
                    break;
                case CompletionType.Channel:
                    CelesteNetPlayerListComponent playerlist = (CelesteNetPlayerListComponent) Context.Components[typeof(CelesteNetPlayerListComponent)];
                    IEnumerable<string> channelNames = playerlist?.Channels?.List?.Select(channel => channel.Name);
                    if (Settings.PlayerListUI.HideOwnChannelName)
                        // don't accidentally leak the channel name via tab completions
                        channelNames = channelNames.Where(name => name != CurrentChannelName);
                    Completion = channelNames.Where(name => name.StartsWith(partial, StringComparison.InvariantCultureIgnoreCase)).ToList() ?? Completion;

                    break;

                case CompletionType.Emoji:
                    IEnumerable<string> filter_emotes = Emoji.Registered.Where(name => !name.StartsWith("celestenet_avatar_"));

                    if (string.IsNullOrEmpty(partial)) {
                        Completion = filter_emotes.ToList();
                    } else {
                        Completion = filter_emotes.Where(name => name.StartsWith(partial)).ToList();
                    }

                    break;

                case CompletionType.Emote:
                    if (partial.Length < 2) {
                        CompletionEmoteAtlas = null;
                        break;
                    }

                    if (CompletionEmoteAtlas == null) {
                        // NOTE: GetIconAtlas actually strips the "i:" / "g:" / "p:" prefix off the ref string parameter
                        CompletionEmoteAtlas = GhostEmote.GetIconAtlas(ref partial);
                        partial = partial.Trim();
                    } else {
                        partial = partial.Substring(2).Trim();
                    }

                    if (CompletionEmoteAtlas != null) {
                        int lastSpace = partial.LastIndexOf(GhostEmote.IconPathsSeperator);

                        // if completing e.g. "madeline/angry ghost/a" then
                        // prefix = "madeline/angry " (not used to find matches)
                        // subpartial = "ghost/a"
                        string prefix = lastSpace == -1 ? "" : partial.Substring(0, lastSpace + 1);
                        string subpartial = lastSpace == -1 ? partial : partial.Substring(lastSpace + 1);

                        // throw the "i:" / "g:" / "p:" back into prefix
                        prefix = CompletionPartial.Substring(0, 2) + " " + prefix;

                        foreach (string key in CompletionEmoteAtlas.Textures.Keys) {
                            if (!key.StartsWith(subpartial))
                                continue;

                            string basename = key.TrimEnd("0123456789".ToCharArray());
                            string subkey = subpartial.Length >= basename.Length ? key : basename;

                            // if partial input is shorter than this key, try to find a '/' later in the key
                            int i = subpartial.Length < key.Length ? key.IndexOf('/', subpartial.Length) : -1;

                            if (i != -1)
                                subkey = key.Substring(0, i + 1);

                            subkey = subkey.Trim();

                            string full_completion = prefix + subkey;
                            if (!Completion.Contains(full_completion))
                                Completion.Add(full_completion);
                        }
                    }
                    break;
            }
        }

        protected override void Render(GameTime gameTime, bool toBuffer) {
            float scale = Scale;

            if (!Active)
                scale *= (1f + Settings.ChatUI.NewMessagesSizeAdjust/10f);

            Vector2 fontScale = Vector2.One * scale;

            RenderPositionY = null;

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
                    float logLength = Settings.ChatUI.ChatLogLength;
                    int renderedCount = 0;
                    skippedMsgCount = 0;
                    int count = ScrolledFromIndex > 0 ? ScrolledFromIndex : log.Count;
                    for (int i = 0; i < count; i++) {
                        DataChat msg = log[count - 1 - i];

                        float alpha = Completion.Count == 0 ? 1f : 0.8f;
                        float deltaToFade = (float) (now - msg.ReceivedDate).TotalSeconds - Settings.ChatUI.NewMessagesFadeTime / 2f;
                        if (!Active && deltaToFade > 0f)
                            alpha = 1f - Ease.CubeIn(deltaToFade);
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

                        Context.RenderHelper.Rect(25f * scale, y, size.X + 50f * scale, height - cutoff, Color.Black * CustomAlpha * alpha);
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

                    RenderPositionY = y;

                    if (Active && Settings.ChatUI.ShowScrollingControls) {
                        if (activeController != Input.GuiInputController())
                            UpdateScrollPromptControls();

                        float x = 25f * scale;
                        y -= 2 * ScrollPromptSize.Y * scale;

                        bool scrollingUp = InputScrollUpState == 1 && renderedCount > 1;
                        bool scrollingDown = InputScrollDownState == 1 && ScrolledDistance > 0f;

                        RenderScrollPrompt(new(x, y), scale, scrollingUp, scrollingDown);
                    }
                }
            }

            if (Active) {
                RenderInputPrompt(
                    new(25f * scale, UI_HEIGHT - 125f * scale),
                    new(UI_WIDTH - 50f * scale, 100f * scale),
                    scale,
                    fontScale,
                    out float promptWidth
                );

                if (ScrolledFromIndex > 0)
                    skippedMsgCount += Log.Count - ScrolledFromIndex;

                if (Typing.Length > 0 && skippedMsgCount > 0) {
                    SetPromptMessage(
                        PromptMessageTypes.Scroll,
                        $"({skippedMsgCount} newer message{(skippedMsgCount > 1 ? "s" : "")} off-screen!)",
                        Color.Orange * .9f
                    );
                } else if (ScrolledFromIndex > 0 && ScrolledFromIndex < Log.Count) {
                    SetPromptMessage(
                        PromptMessageTypes.Scroll,
                        $"({Log.Count - ScrolledFromIndex} new message{(Log.Count - ScrolledFromIndex > 1 ? "s" : "")} since you scrolled up!)",
                        Color.GreenYellow
                    );
                } else if (PromptMessageType == PromptMessageTypes.Scroll) {
                    SetPromptMessage(PromptMessageTypes.None);
                }

                CelesteNetClientFont.Draw(
                    PromptMessage,
                    new(200f * scale + CelesteNetClientFont.Measure(Typing).X * scale, UI_HEIGHT - 105f * scale),
                    Vector2.Zero,
                    fontScale,
                    PromptMessageColor
                );
                RenderCompletions(new(25f * scale + promptWidth, UI_HEIGHT - 125f * scale), scale, fontScale);
            }
        }

        protected void UpdateScrollPromptControls()
        {
            InputScrollUpIcon = Input.GuiButton(Settings.ButtonChatScrollUp.Button, mode: Input.PrefixMode.Latest);
            InputScrollDownIcon = Input.GuiButton(Settings.ButtonChatScrollDown.Button, mode: Input.PrefixMode.Latest);
            ScrollPromptSize.X = ArrowUpIcon.Width + InputScrollUpIcon?.Width ?? 0f;
            ScrollPromptSize.Y = Math.Max(ArrowUpIcon.Height, InputScrollUpIcon?.Height ?? 0f);
            activeController = Input.GuiInputController();
        }

        protected void RenderInputPrompt(Vector2 pos, Vector2 size, float scale, Vector2 fontScale, out float promptWidth) {
            Context.RenderHelper.Rect(pos.X, pos.Y, size.X, size.Y, Color.Black * 0.8f);
            pos.X += 25f * scale;
            pos.Y += 20f * scale;

            CelesteNetClientFont.Draw(
                ">",
                pos,
                Vector2.Zero,
                fontScale * new Vector2(0.5f, 1f),
                Color.White * 0.5f
            );
            promptWidth = CelesteNetClientFont.Measure(">").X * scale;
            pos.X += promptWidth;

            string text = Typing;
            CelesteNetClientFont.Draw(
                text,
                pos,
                Vector2.Zero,
                fontScale,
                Color.White
            );

            if (!Calc.BetweenInterval(_Time, 0.5f)) {
                if (CursorIndex == Typing.Length) {
                    pos.X += CelesteNetClientFont.Measure(text).X * scale;
                    CelesteNetClientFont.Draw(
                        "_",
                        pos,
                        Vector2.Zero,
                        fontScale,
                        Color.White * 0.5f
                    );
                } else {
                    // draw cursor at correct location, but move back half a "." width to not overlap following char
                    pos.X += CelesteNetClientFont.Measure(Typing.Substring(0, CursorIndex)).X * scale;
                    pos.X -= CelesteNetClientFont.Measure(".").X / 2f * scale;
                    pos.Y -= 5f * scale;

                    CelesteNetClientFont.Draw(
                        "|",
                        pos,
                        Vector2.Zero,
                        fontScale * new Vector2(.5f, 1.2f),
                        Color.White * 0.6f
                    );
                }
            }
        }

        protected void RenderScrollPrompt(Vector2 pos, float scale, bool upActive, bool downActive) {
            Context.RenderHelper.Rect(pos.X, pos.Y, 50f * scale + ScrollPromptSize.X * scale, 2 * ScrollPromptSize.Y * scale, Color.Black * CustomAlpha);
            pos.X += 25f * scale;

            float oldPosX = pos.X;

            // top
            if (InputScrollUpIcon != null)
            {
                InputScrollUpIcon.Draw(
                    pos,
                    Vector2.Zero,
                    upActive ? Color.Goldenrod : Color.White,
                    scale
                );
                pos.X += InputScrollUpIcon.Width * scale;
            }

            ArrowUpIcon.Draw(
                pos,
                Vector2.Zero,
                Color.White * (upActive ? 1f : .7f),
                scale
            );

            pos.X = oldPosX;
            pos.Y += ScrollPromptSize.Y * scale;

            // bottom
            if (InputScrollDownIcon != null)
            {
                InputScrollDownIcon.Draw(
                    pos,
                    Vector2.Zero,
                    downActive ? Color.Goldenrod : Color.White,
                    scale
                );
                pos.X += InputScrollDownIcon.Width * scale;
            }

            ArrowDownIcon.Draw(
                pos,
                Vector2.Zero,
                Color.White * (downActive ? 1f : .7f),
                scale
            );
        }

        protected void RenderCompletions(Vector2 pos, float scale, Vector2 fontScale) {
            Vector2 curPos = pos;

            for (int i = 0; i < Completion.Count; i++) {
                string match = Completion[i];
                string alias = "";
                if (CompletionArgType == CompletionType.Command)
                    CommandAliasLookup.TryGetValue(match, out alias);

                /* An example input prompt with suggestion:
                 *   /w RedFlames   (what we're rendering)
                 * > /w R           (input prompt)
                 * 
                 * prefix = "/w ", match = "RedFlames", typed = "R", suggestion = "edFlames"
                 */
                string typed = "", suggestion = "", suggestionPrefix = "", suggestionSuffix = "";
                string prefix = Typing.Substring(0, _CursorIndex - CompletionPartial.Length);

                if (match.StartsWith(CompletionPartial, StringComparison.InvariantCultureIgnoreCase)) {
                    typed = match.Substring(0, CompletionPartial.Length);
                    suggestion = match.Substring(CompletionPartial.Length);
                } else {
                    suggestion = match;
                }

                // since e.g. typing /channelcha + [Tab] gets you /cc, it will look like "/channelchat -> /cc"
                if (!alias.IsNullOrEmpty())
                    suggestionSuffix = " -> /" + alias;

                // prefix "emoji" completions with a preview of the actual emoji, and surround with ":"
                if (CompletionArgType == CompletionType.Emoji) {
                    if (Emoji.TryGet(match, out char emoji))
                        suggestionPrefix = $"{emoji} :";
                    else
                        suggestionPrefix = ":";
                    suggestion += ":";
                }

                Vector2 sizePrefix = CelesteNetClientFont.Measure(prefix);
                Vector2 sizeTyped = CelesteNetClientFont.Measure(typed);
                Vector2 sizeSuggestionPrefix = CelesteNetClientFont.Measure(suggestionPrefix);
                Vector2 sizeSuggestion = CelesteNetClientFont.Measure(suggestion);
                Vector2 sizeSuggestionSuffix = CelesteNetClientFont.Measure(suggestionSuffix);

                // these are mainly for the background rect
                float width = sizeSuggestionPrefix.X + sizeTyped.X + sizeSuggestion.X + sizeSuggestionSuffix.X + 50f;
                float height = 5f + Math.Max(sizeTyped.Y, sizeSuggestion.Y);

                curPos.X = pos.X - sizeSuggestionPrefix.X * scale;
                curPos.Y -= height * scale;

                // "emoji" completion doesn't render "prefix", only suggestion
                if (CompletionArgType == CompletionType.Emoji) {
                    curPos.X += sizePrefix.X * scale;
                } else {
                    width += sizePrefix.X;
                }

                Context.RenderHelper.Rect(curPos.X, curPos.Y, width * scale, height * scale, Color.Black * 0.8f);
                curPos.X += 25f * scale;

                // default colors of the text parts
                Color colorPrefix = Color.DarkGray;
                Color colorTyped = Color.Gray;
                Color colorSuggestionPrefix = Color.White;
                Color colorSuggestion = Color.Gold;
                Color colorSuggestionSuffix = Color.DarkGray * 0.8f;

                // fade all other suggestions if one is selected
                if (CompletionSelected > -1 && CompletionSelected != i) {
                    colorSuggestion = Color.Lerp(Color.Gold, Color.Gray, .66f);
                }

                // give green-ish color to selected, or if only one exists (Tab completes without having to select)
                if (CompletionSelected == i || Completion.Count == 1) {
                    colorTyped = colorSuggestion = Color.Lerp(Color.Gold, Color.GreenYellow, .66f);
                }

                if (CompletionArgType != CompletionType.Emoji) {
                    CelesteNetClientFont.Draw(
                        prefix,
                        curPos,
                        Vector2.Zero,
                        fontScale,
                        colorPrefix
                    );
                    curPos.X += sizePrefix.X * scale;
                }

                CelesteNetClientFont.Draw(
                    suggestionPrefix,
                    curPos,
                    Vector2.Zero,
                    fontScale,
                    colorSuggestionPrefix
                );
                curPos.X += sizeSuggestionPrefix.X * scale;

                CelesteNetClientFont.Draw(
                    typed,
                    curPos,
                    Vector2.Zero,
                    fontScale,
                    colorTyped
                );
                curPos.X += sizeTyped.X * scale;

                CelesteNetClientFont.Draw(
                    suggestion,
                    curPos,
                    Vector2.Zero,
                    fontScale,
                    colorSuggestion
                );
                curPos.X += sizeSuggestion.X * scale;

                CelesteNetClientFont.Draw(
                    suggestionSuffix,
                    curPos,
                    Vector2.Zero,
                    fontScale,
                    colorSuggestionSuffix
                );
            }
        }

        protected override void Dispose(bool disposing) {
            // important because of setter side-effects, see comment there
            Active = false;

            base.Dispose(disposing);
        }

    }
}
