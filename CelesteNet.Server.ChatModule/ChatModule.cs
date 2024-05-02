using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat.Cmd;
using Microsoft.Xna.Framework;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatModule : CelesteNetServerModule<ChatSettings> {

        public readonly ConcurrentDictionary<uint, DataChat> ChatLog = new();
        public readonly RingBuffer<DataChat?> ChatBuffer = new(3000);
        public uint NextID = (uint) (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);

#pragma warning disable CS8618 // Set on init.
        public SpamContext BroadcastSpamContext;
        public CommandsContext Commands;
#pragma warning restore CS8618

        private HashSet<string> filterDrop = new();
        private HashSet<string> filterKick = new();
        private HashSet<string> filterWarnOnce = new();

        public override void Init(CelesteNetServerModuleWrapper wrapper) {
            base.Init(wrapper);

            BroadcastSpamContext = new(this);
            Commands = new(this);
            Server.OnSessionStart += OnSessionStart;
            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession session in Server.Sessions)
                    session.OnEnd += OnSessionEnd;
        }

        public override void SaveSettings() {
            base.SaveSettings();

            UpdateFilterLists();
        }

        public override void LoadSettings() {
            base.LoadSettings();

            UpdateFilterLists();
        }

        public void UpdateFilterLists() {
            filterDrop.Clear();
            filterKick.Clear();
            filterWarnOnce.Clear();

            if (Settings.FilterDrop != null) {
                foreach (string word in Settings.FilterDrop) {
                    filterDrop.Add(word.ToLower().Trim());
                }
                Logger.Log(LogLevel.INF, "chat", $"FilterDrop: {filterDrop.Count} entries.");
            }

            if (Settings.FilterKick != null) {
                foreach (string word in Settings.FilterKick) {
                    filterKick.Add(word.ToLower().Trim());
                }
                Logger.Log(LogLevel.INF, "chat", $"FilterKick: {filterKick.Count} entries.");
            }

            if (Settings.FilterWarnOnce != null) {
                foreach (string word in Settings.FilterWarnOnce) {
                    filterWarnOnce.Add(word.ToLower().Trim());
                }
                Logger.Log(LogLevel.INF, "chat", $"FilterWarnOnce: {filterWarnOnce.Count} entries.");
            }

        }

        public override void Dispose() {
            base.Dispose();

            BroadcastSpamContext.Dispose();

            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession session in Server.Sessions)
                    session.Remove<SpamContext>(this)?.Dispose();

            Server.OnSessionStart -= OnSessionStart;
            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession session in Server.Sessions)
                    session.OnEnd -= OnSessionEnd;
        }

        public FilterHandling IsFilteredWord(string word, bool toLowerTrim = false) {
            // optional param I guess as a sort of reminder that filter words are always checked in lowercase - but
            // currently the only calls are below in ContainsFilteredWord and are guaranteed to be lower & trimmed
            if (toLowerTrim)
                word = word.ToLower().Trim();

            FilterHandling filter = FilterHandling.None;

            if (filterDrop.Contains(word))
                filter |= FilterHandling.Drop;

            if (filterKick.Contains(word))
                filter |= FilterHandling.Kick;

            if (filterWarnOnce.Contains(word))
                filter |= FilterHandling.WarnOnce;

            return filter;
        }

        public FilterHandling ContainsFilteredWord(string text) {
            if (filterDrop.Count + filterKick.Count + filterWarnOnce.Count == 0)
                return FilterHandling.None;

            FilterHandling sumChecks = FilterHandling.None;

            // had some code here to replace all whitespace with actual 0x20, but per CelesteNetUtils.EnglishFontChars we strip all besides 0x20
            text = text.ToLower().Trim();

            // this function uses two relatively basic ways of splitting text into words for checking
            StringBuilder builderStripped = new StringBuilder(text.Length);
            StringBuilder builderSpaced = new StringBuilder(text.Length);

            // adding the extra space to always 'trigger' handling the last word, rather than doing it after the loop
            foreach (char c in text + ' ') {
                // only letters and digits are used for comparing against filter lists
                if (char.IsAsciiLetterOrDigit(c)) {
                    builderStripped.Append(c);
                    builderSpaced.Append(c);
                    continue;
                } else if (c == ' ' && builderStripped.Length > 0) {
                    // builderStripped is simply checked on every space and then cleared, as if it were a foreach text.Split(' ')
                    sumChecks |= IsFilteredWord(builderStripped.ToString());
                    builderStripped.Clear();
                }
                // builderSpaced is checked any time a non-alphanumeric character or space is found (hence no 'continue' above for spaces)
                // so instead of stripping out the other characters inside words, the words are split at non-alphanumeric as well
                if (builderSpaced.Length > 0) {
                    sumChecks |= IsFilteredWord(builderSpaced.ToString());
                    builderSpaced.Clear();
                }
            }

            // NOTE: Things this could also be doing but currently doesn't
            // - strip out numbers or split at numbers
            // - replace 1337-speak numbers like 1 -> i, 0 -> o, ...
            // - but at what point does the matching get so fuzzy that we should rather ping the humans?

            return sumChecks;
        }

        public event Action<ChatModule, FilterDecision>? OnApplyFilter;
        // I'm sure having this extra method is a crime but who's gonna stop me (using this in CmdChannel when filtering channel names)
        public void InvokeOnApplyFilter(FilterDecision chatFilterDecision) => OnApplyFilter?.Invoke(this, chatFilterDecision);

        public FilterHandling ApplyFilterHandling(CelesteNetPlayerSession session, DataChat msg, FilterHandling filterAs = FilterHandling.None) {
            // can set the optional parameter to override word checking
            if (filterAs == FilterHandling.None)
                filterAs = ContainsFilteredWord(msg.Text);

            FilterHandling handledAs = FilterHandling.None;

            if (filterAs.HasFlag(FilterHandling.Kick)) {
                session.Con.Send(new DataDisconnectReason { Text = $"Disconnected: " + Settings.MessageDefaultKickReason });
                session.Con.Send(new DataInternalDisconnect());
                session.Dispose();
                handledAs = FilterHandling.Kick;
            } else if (filterAs.HasFlag(FilterHandling.Drop)) {
                if (msg.Player != null) {
                    // ack the message to clear Pending, but noone else will see it
                    msg.Targets = [msg.Player];
                    ForceSend(msg);
                }
                handledAs = FilterHandling.Drop;
            } else if (filterAs.HasFlag(FilterHandling.WarnOnce)) {
                string? warnedOnceFor = new DynamicData(session).Get<string>("warnedOnceFor");

                if (!warnedOnceFor.IsNullOrEmpty() && !msg.Text.IsNullOrEmpty() && warnedOnceFor == msg.Text) {
                    // Player has been warned once, letting this through but might get reviewed by moderators
                    handledAs = FilterHandling.None;
                } else if (!msg.Text.IsNullOrEmpty()) {
                    if (msg.Player != null) {
                        // ack the message to clear Pending, but noone else will see it
                        msg.Targets = [msg.Player];
                        ForceSend(msg);
                    }

                    SendTo(session, Settings.MessageWarnOnce, null, Settings.ColorError);

                    new DynamicData(session).Set("warnedOnceFor", msg.Text);
                    handledAs = FilterHandling.WarnOnce;
                }
            }

            if (handledAs != FilterHandling.None) {
                Logger.Log(LogLevel.INF, "word-filter", $"Message '{msg.Text}' triggered {handledAs} handling. ({filterAs})");
                OnApplyFilter?.Invoke(this, new FilterDecision(msg) { Handling = handledAs });
            }

            return handledAs;
        }

        private void OnSessionStart(CelesteNetPlayerSession session) {
            if (Settings.FilterPlayerNames != FilterHandling.None && session.PlayerInfo != null) {
                FilterHandling check = ContainsFilteredWord(session.PlayerInfo.FullName);
                if (check != FilterHandling.None && Settings.FilterPlayerNames.HasFlag(check)) {
                    Logger.Log(LogLevel.INF, "word-filter", $"Disconnecting: Name '{session.PlayerInfo.FullName}' triggered handling '{check}'.");
                    OnApplyFilter?.Invoke(this, new FilterDecision(session.PlayerInfo) {
                        Handling = FilterHandling.Kick,
                        Cause = FilterDecisionCause.UserName
                    });
                    session.Con.Send(new DataDisconnectReason { Text = $"Disconnected: Name '{session.PlayerInfo.FullName}' not acceptable." });
                    session.Con.Send(new DataInternalDisconnect());
                    session.Dispose();
                    return;
                }
            }

            if (!session.ClientOptions.IsReconnect) {
                if (Settings.GreetPlayers)
                    Broadcast(Settings.MessageGreeting.InjectSingleValue("player", session.PlayerInfo?.FullName ?? "???"));
                SendTo(session, Settings.MessageMOTD);
            }
            session.SendCommandList(Commands.DataAll);

            SpamContext spam = session.Set(this, new SpamContext(this));
            spam.OnSpam += (msg, timeout) => {
                if (session.PlayerInfo == null)
                    return;
                msg.Targets = [session.PlayerInfo];
                ForceSend(msg);
                SendTo(session, Settings.MessageSpam, null, Settings.ColorError);
            };
            session.OnEnd += OnSessionEnd;
        }

        private void OnSessionEnd(CelesteNetPlayerSession session, DataPlayerInfo? lastPlayerInfo) {
            string? sessionName = lastPlayerInfo?.FullName;
            if (!sessionName.IsNullOrEmpty()) {
                string? reason = new DynamicData(session).Get<string>("leaveReason");
                if (Settings.GreetPlayers || !string.IsNullOrEmpty(reason))
                    Broadcast((reason ?? Settings.MessageLeave).InjectSingleValue("player", sessionName));
            }
            session.Remove<SpamContext>(this)?.Dispose();
        }

        public DataChat? PrepareAndLog(CelesteNetPlayerSession? session, DataChat msg, bool invokeReceive = true) {
            lock (ChatLog)
                msg.ID = NextID++;
            msg.Date = DateTime.UtcNow;

            if (msg.Text.Length == 0)
                return null;

            if (!msg.CreatedByServer) {
                // This condition matches DataChat when it's been read from a CelesteNetBinaryReader

                if (session == null || session.PlayerInfo == null)
                    return null;

                msg.Player = session.PlayerInfo;
                msg.Text = msg.Text.Trim().Replace("\r", "").Replace("\n", "");

                if (msg.Text.IsNullOrEmpty())
                    return null;

                if (msg.Text.Length > Settings.MaxChatTextLength)
                    msg.Text = msg.Text.Substring(0, Settings.MaxChatTextLength);

                msg.Tag = "";
                msg.Color = Color.White;

                if (session.Get<SpamContext>(this)?.IsSpam(msg) ?? false)
                    return null;

                bool isGlobalChat = !Server.UserData.Load<UserChatSettings>(session.UID).AutoChannelChat;

                // word filtering for command invocations will be done within the commands
                if (!msg.Text.StartsWith(Settings.CommandPrefix) && (isGlobalChat || !Settings.FilterOnlyGlobalAndMainChat)) {
                    if (ApplyFilterHandling(session, msg) != FilterHandling.None)
                        return null;
                }

                if (filterWarnOnce.Count > 0) {
                    Logger.Log(LogLevel.DEV, "word-filter", $"Resetting warnedOnceFor for {session}.");
                    new DynamicData(session).Set("warnedOnceFor", null);
                }

            } else if (msg.Player != null && (msg.Targets == null || msg.Targets.Length > 0)) {
                /* This condition matches messages created by server but with a valid Player:
                 *  - messages generated from a /cc, /gc or /w chat by a player
                 *  - the chat module logging emotes in a handler further down here
                 *  
                 *  The second part of the conditional matches any message where:
                 *  - Targets is null or
                 *  - Targets isn't an empty array?...
                 */

                // Is the sole practical purpose of this clause to spam-filter /gc'd and /cc'd chats properly?

                if (!Server.PlayersByID.TryGetValue(msg.Player.ID, out CelesteNetPlayerSession? player))
                    return null;

                if (player.Get<SpamContext>(this)?.IsSpam(msg) ?? false)
                    return null;

            } else if ((msg.Targets == null || msg.Targets.Length == 0) && BroadcastSpamContext.IsSpam(msg)) {
                // if we're here, msg.Player must be null
                return null;
            }

            lock (ChatBuffer) {
                DataChat? prev = ChatBuffer.Get();
                if (prev != null)
                    ChatLog.TryRemove(prev.ID, out _);
                ChatLog[msg.ID] = msg;
                ChatBuffer.Set(msg).Move(1);
            }

            if (!msg.CreatedByServer)
                Logger.Log(LogLevel.INF, "chatmsg", msg.ToString(false, true));

            if (invokeReceive)
                OnReceive?.Invoke(this, msg);

            return msg;
        }

        public event Action<ChatModule, DataChat>? OnReceive;

        public void Handle(CelesteNetConnection? con, DataChat msg) {
            // don't dedupe the text messages, they should repeat very rarely
            msg.Text = msg.Text.Sanitize(null, true, false);

            CelesteNetPlayerSession? session = null;
            if (con != null)
                Server.PlayersByCon.TryGetValue(con, out session);

            if (PrepareAndLog(session, msg, false) == null)
                return;

            if ((!msg.CreatedByServer || msg.Player == null) && msg.Text.StartsWith(Settings.CommandPrefix)) {
                if (msg.Player != null) {
                    // Player should at least receive msg ack.
                    msg.Color = Settings.ColorCommand;
                    msg.Targets = [msg.Player];
                    ForceSend(msg);
                }

                // TODO: Improve or rewrite. This comes from GhostNet, which adopted it from disbot (0x0ade's C# Discord bot).

                CmdEnv env = new(this, msg);

                string cmdName = msg.Text.Substring(Settings.CommandPrefix.Length);
                cmdName = cmdName.Split(ChatCmd.NameDelimiters)[0].ToLowerInvariant();
                if (cmdName.Length == 0)
                    return;

                ChatCmd? cmd = Commands.Get(cmdName);
                if (cmd != null) {
                    env.Cmd = cmd;
                    Task.Run(() => {
                            cmd.ParseAndRun(env);
                    });

                } else {
                    env.Send($"Command {cmdName} not found!", color: Settings.ColorError);
                }

                return;
            }

            if (msg.Player != null && Server.PlayersByID.TryGetValue(msg.Player.ID, out session) &&
                Server.UserData.Load<UserChatSettings>(session.UID).AutoChannelChat) {
                msg.Targets = [msg.Player];
                Commands.Get<CmdChannelChat>().ParseAndRun(new CmdEnv(this, msg));
                return;
            }

            OnReceive?.Invoke(this, msg);

            if (msg.Targets == null) {
                Server.BroadcastAsync(msg);
                return;
            }

            DataInternalBlob blob = new(Server.Data, msg);
            foreach (DataPlayerInfo playerInfo in msg.Targets)
                if (Server.PlayersByID.TryGetValue(playerInfo.ID, out CelesteNetPlayerSession? player))
                    player.Con?.Send(blob);
        }

        public void Handle(CelesteNetConnection con, DataEmote emote) {
            if (con == null)
                return;

            if (!Server.PlayersByCon.TryGetValue(con, out CelesteNetPlayerSession? player))
                return;

            DataPlayerInfo? playerInfo = player.PlayerInfo;
            if (playerInfo == null)
                return;

            DataChat msg = new() {
                Player = playerInfo,
                Targets = Array.Empty<DataPlayerInfo>(),
                Text = emote.Text,
                Tag = "emote",
                Color = Settings.ColorLogEmote
            };

            if (Settings.LogEmotes) {
                PrepareAndLog(player, msg);
            } else {
                Logger.Log(LogLevel.INF, "chatemote", msg.ToString(false, true));
            }
        }


        public DataChat Broadcast(string text, string? tag = null, Color? color = null, DataPlayerInfo[]? targets = null) {
            DataChat msg = new() {
                Text = text,
                Tag = tag ?? "",
                Color = color ?? Settings.ColorBroadcast,
                Targets = targets
            };
            Broadcast(msg);
            return msg;
        }

        public void Broadcast(DataChat msg) {
            Logger.Log(LogLevel.INF, "chat", $"Broadcasting: {msg.Text}");
            Handle(null, msg);
        }

        public DataChat? SendTo(CelesteNetPlayerSession? player, string text, string? tag = null, Color? color = null) {
            DataChat msg = new() {
                Text = text,
                Tag = tag ?? "",
                Color = color ?? Settings.ColorServer
            };
            if (player?.PlayerInfo == null) {
                Logger.Log(LogLevel.INF, "chat", $"Sending to nobody: {text}");
                PrepareAndLog(null, msg);
                return null;
            }

            Logger.Log(LogLevel.INF, "chat", $"Sending to {player.PlayerInfo}: {text}");

            msg.Targets = [player.PlayerInfo];

            player.Con.Send(PrepareAndLog(null, msg));
            return msg;
        }

        public event Action<ChatModule, DataChat>? OnForceSend;

        public void ForceSend(DataChat msg) {
            msg.Version++;
            Logger.Log(LogLevel.INF, "chatupd", msg.ToString(false, true));
            OnForceSend?.Invoke(this, msg);

            if (msg.Targets == null) {
                Server.BroadcastAsync(msg);
                return;
            }

            DataInternalBlob blob = new(Server.Data, msg);
            foreach (DataPlayerInfo playerInfo in msg.Targets)
                if (Server.PlayersByID.TryGetValue(playerInfo.ID, out CelesteNetPlayerSession? player))
                    player.Con?.Send(blob);
        }

        public class UserChatSettings {
            public bool AutoChannelChat { get; set; } = false;
            public bool Whispers { get; set; } = true;
        }
    }

    public enum FilterDecisionCause {
        Chat = 0,
        UserName = 1,
        ChannelName = 2,
    }

    public class FilterDecision {
        public FilterHandling Handling { get; set; } = FilterHandling.None;
        public FilterDecisionCause Cause { get; set; } = FilterDecisionCause.Chat;
        public uint chatID { get; set; } = uint.MaxValue;
        public string chatTag { get; set; } = string.Empty;
        public string chatText { get; set; } = string.Empty;

        public string playerName { get; set; } = string.Empty;
        public uint playerID { get; set; } = uint.MaxValue;

        public FilterDecision() {
        }

        public FilterDecision(DataPlayerInfo? player) {
            playerID = player?.ID ?? uint.MaxValue;
            playerName = player?.FullName ?? "";
        }

        public FilterDecision(DataChat fromChat) {
            chatID = fromChat.ID;
            chatTag = fromChat.Tag;
            chatText = fromChat.Text;
            playerID = fromChat.Player?.ID ?? uint.MaxValue;
            playerName = fromChat.Player?.FullName ?? "";
        }
    }

}
