using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

        private HashSet<string> filterDrop = new HashSet<string>();
        private HashSet<string> filterKick = new HashSet<string>();
        private HashSet<string> filterWarnOnce = new HashSet<string>();

        public override void Init(CelesteNetServerModuleWrapper wrapper) {
            base.Init(wrapper);

            UpdateFilterLists();

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

        public void UpdateFilterLists() {
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

        public FilterHandling IsFilteredWord(string word) {
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
            if ((filterDrop.Count + filterKick.Count + filterWarnOnce.Count) == 0)
                return FilterHandling.None;

            text = Regex.Replace(text, @"\s", " ").ToLower().Trim();

            string textStripped = Regex.Replace(text, @"[^a-zA-Z0-9 ]", "");
            string textSpaced = Regex.Replace(text, @"[^a-zA-Z0-9]", " ");

            Logger.Log(LogLevel.DEV, "word-filter", "[" + string.Join(", ", textStripped.Split(' ')) + "]");
            Logger.Log(LogLevel.DEV, "word-filter", "[" + string.Join(", ", textSpaced.Split(' ')) + "]");

            FilterHandling sumChecks = FilterHandling.None;
            foreach (var filter in textStripped.Split(' ').Select(IsFilteredWord))
                sumChecks |= filter;

            foreach (var filter in textSpaced.Split(' ').Select(IsFilteredWord))
                sumChecks |= filter;

            return sumChecks;
        }

        public event Action<ChatModule, DataChat, FilterHandling>? OnApplyFilter;

        public FilterHandling ApplyFilterHandling(CelesteNetPlayerSession session, DataChat msg) {
            return ApplyFilterHandling(session, ContainsFilteredWord(msg.Text), msg);
        }

        public FilterHandling ApplyFilterHandling(CelesteNetPlayerSession session, FilterHandling filter, DataChat msg) {
            FilterHandling handled = FilterHandling.None;

            if (filter.HasFlag(FilterHandling.Kick)) {
                Logger.Log(LogLevel.INF, "word-filter", $"Message '{msg.Text}' triggered Kick handling. ({filter})");
                session.Con.Send(new DataDisconnectReason { Text = $"Disconnected: " + Settings.MessageDefaultKickReason });
                session.Con.Send(new DataInternalDisconnect());
                session.Dispose();
                handled = FilterHandling.Kick;
            } else if (filter.HasFlag(FilterHandling.Drop)) {
                if (msg.Player != null) {
                    // ack the message to clear Pending, but noone else will see it
                    msg.Targets = [msg.Player];
                    ForceSend(msg);
                }
                handled = FilterHandling.Drop;
            } else if (filter.HasFlag(FilterHandling.WarnOnce)) {
                string? warnedOnceFor = new DynamicData(session).Get<string>("warnedOnceFor");

                if (!warnedOnceFor.IsNullOrEmpty() && !msg.Text.IsNullOrEmpty() && warnedOnceFor == msg.Text) {
                    // Player has been warned once, letting this through but might get reviewed by moderators
                    handled = FilterHandling.None;
                } else if (!msg.Text.IsNullOrEmpty()) {
                    if (msg.Player != null) {
                        // ack the message to clear Pending, but noone else will see it
                        msg.Targets = [msg.Player];
                        ForceSend(msg);
                    }

                    SendTo(session, Settings.MessageWarnOnce, null, Settings.ColorError);

                    new DynamicData(session).Set("warnedOnceFor", msg.Text);
                    handled = FilterHandling.WarnOnce;
                }
            }

            if (handled != FilterHandling.None) {
                Logger.Log(LogLevel.INF, "word-filter", $"Message '{msg.Text}' triggered {handled} handling. ({filter})");
                OnApplyFilter?.Invoke(this, msg, handled);
            }

            return handled;
        }

        private void OnSessionStart(CelesteNetPlayerSession session) {
            if (Settings.FilterPlayerNames != FilterHandling.None && session.PlayerInfo != null) {
                FilterHandling check = ContainsFilteredWord(session.PlayerInfo.FullName);
                if (check != FilterHandling.None && Settings.FilterPlayerNames.HasFlag(check)) {
                    Logger.Log(LogLevel.INF, "word-filter", $"Disconnecting: Name '{session.PlayerInfo.FullName}' triggered handling '{check}'.");
                    session.Con.Send(new DataDisconnectReason { Text = $"Disconnected: Name '{session.PlayerInfo.FullName}' not acceptable." });
                    session.Con.Send(new DataInternalDisconnect());
                    session.Dispose();
                    return;
                }
            }

            if (!session.ClientOptions.IsReconnect) {
                if (Settings.GreetPlayers)
                    Broadcast(Settings.MessageGreeting.InjectSingleValue("player", session.PlayerInfo?.DisplayName ?? "???"));
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
            string? displayName = lastPlayerInfo?.DisplayName;
            if (!displayName.IsNullOrEmpty()) {
                string? reason = new DynamicData(session).Get<string>("leaveReason");
                if (Settings.GreetPlayers || !string.IsNullOrEmpty(reason))
                    Broadcast((reason ?? Settings.MessageLeave).InjectSingleValue("player", displayName));
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

            if (msg.Targets == null){
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

        public void Broadcast(DataChat msg)
        {
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
}
