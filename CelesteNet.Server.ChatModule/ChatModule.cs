using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatModule : CelesteNetServerModule<ChatSettings> {

        public readonly ConcurrentDictionary<uint, DataChat> ChatLog = new();
        public readonly RingBuffer<DataChat?> ChatBuffer = new(3000);
        public uint NextID = (uint) (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);

#pragma warning disable CS8618 // Set on init.
        public SpamContext BroadcastSpamContext;
        public ChatCommands Commands;
#pragma warning restore CS8618

        public override void Init(CelesteNetServerModuleWrapper wrapper) {
            base.Init(wrapper);

            BroadcastSpamContext = new(this);
            Commands = new(this);
            Server.OnSessionStart += OnSessionStart;
            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession session in Server.Sessions)
                    session.OnEnd += OnSessionEnd;
        }

        public override void Dispose() {
            base.Dispose();

            BroadcastSpamContext.Dispose();
            Commands.Dispose();

            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession session in Server.Sessions)
                    session.Remove<SpamContext>(this)?.Dispose();

            Server.OnSessionStart -= OnSessionStart;
            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession session in Server.Sessions)
                    session.OnEnd -= OnSessionEnd;
        }

        private void OnSessionStart(CelesteNetPlayerSession session) {
            if (!session.ClientOptions.IsReconnect) {
                if (Settings.GreetPlayers)
                    Broadcast(Settings.MessageGreeting.InjectSingleValue("player", session.PlayerInfo?.DisplayName ?? "???"));
                SendTo(session, Settings.MessageMOTD);
            }
            session.SendCommandList(Commands.DataAll);

            SpamContext spam = session.Set(this, new SpamContext(this));
            spam.OnSpam += (msg, timeout) => {
                msg.Target = session.PlayerInfo;
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

        public DataChat? PrepareAndLog(CelesteNetConnection? from, DataChat msg) {
            lock (ChatLog)
                msg.ID = NextID++;
            msg.Date = DateTime.UtcNow;

            if (!msg.CreatedByServer) {
                if (from == null)
                    return null;

                if (!Server.PlayersByCon.TryGetValue(from, out CelesteNetPlayerSession? player))
                    return null;

                msg.Player = player.PlayerInfo;
                if (msg.Player == null)
                    return null;

                msg.Text = msg.Text.Trim();
                if (msg.Text.IsNullOrEmpty())
                    return null;

                msg.Text.Replace("\r", "").Replace("\n", "");
                if (msg.Text.Length > Settings.MaxChatTextLength)
                    msg.Text = msg.Text.Substring(0, Settings.MaxChatTextLength);

                msg.Tag = "";
                msg.Color = Color.White;

                if (player.Get<SpamContext>(this)?.IsSpam(msg) ?? false)
                    return null;

            } else if (msg.Player != null && (msg.Targets?.Length ?? 1) > 0) {
                if (!Server.PlayersByID.TryGetValue(msg.Player.ID, out CelesteNetPlayerSession? player))
                    return null;

                if (player.Get<SpamContext>(this)?.IsSpam(msg) ?? false)
                    return null;

            } else if (msg.Targets == null && BroadcastSpamContext.IsSpam(msg)) {
                return null;
            }

            if (msg.Text.Length == 0)
                return null;

            lock (ChatBuffer) {
                DataChat? prev = ChatBuffer.Get();
                if (prev != null)
                    ChatLog.TryRemove(prev.ID, out _);
                ChatLog[msg.ID] = msg;
                ChatBuffer.Set(msg).Move(1);
            }

            if (!msg.CreatedByServer)
                Logger.Log(LogLevel.INF, "chatmsg", msg.ToString(false, true));

            if (!(OnReceive?.InvokeWhileTrue(this, msg) ?? true))
                return null;

            return msg;
        }

        public event Func<ChatModule, DataChat, bool>? OnReceive;

        public void Handle(CelesteNetConnection? con, DataChat msg) {
            if (PrepareAndLog(con, msg) == null)
                return;

            if ((!msg.CreatedByServer || msg.Player == null) && msg.Text.StartsWith(Settings.CommandPrefix)) {
                if (msg.Player != null) {
                    // Player should at least receive msg ack.
                    msg.Color = Settings.ColorCommand;
                    msg.Target = msg.Player;
                    ForceSend(msg);
                }

                // TODO: Improve or rewrite. This comes from GhostNet, which adopted it from disbot (0x0ade's C# Discord bot).

                ChatCMDEnv env = new(this, msg);

                string cmdName = env.FullText.Substring(Settings.CommandPrefix.Length);
                cmdName = cmdName.Split(ChatCMD.NameDelimiters)[0].ToLowerInvariant();
                if (cmdName.Length == 0)
                    return;

                ChatCMD? cmd = Commands.Get(cmdName);
                if (cmd != null) {
                    env.Cmd = cmd;
                    Task.Run(() => {
                        try {
                            cmd.ParseAndRun(env);
                        } catch (Exception e) {
                            env.Error(e);
                        }
                    });

                } else {
                    env.Send($"Command {cmdName} not found!", color: Settings.ColorError);
                }

                return;
            }

            if (msg.Player != null && Server.PlayersByID.TryGetValue(msg.Player.ID, out CelesteNetPlayerSession? session) &&
                Server.UserData.Load<UserChatSettings>(session.UID).AutoChannelChat) {
                msg.Target = msg.Player;
                Commands.Get<ChatCMDChannelChat>().ParseAndRun(new ChatCMDEnv(this, msg));
                return;
            }

            Server.BroadcastAsync(msg);
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
                Targets = new DataPlayerInfo[0],
                Text = emote.Text,
                Tag = "emote",
                Color = Settings.ColorLogEmote
            };

            if (Settings.LogEmotes) {
                PrepareAndLog(con, msg);
            } else {
                Logger.Log(LogLevel.INF, "chatemote", msg.ToString(false, true));
            }
        }


        public DataChat Broadcast(string text, string? tag = null, Color? color = null) {
            Logger.Log(LogLevel.INF, "chat", $"Broadcasting: {text}");
            DataChat msg = new() {
                Text = text,
                Tag = tag ?? "",
                Color = color ?? Settings.ColorBroadcast
            };
            Handle(null, msg);
            return msg;
        }

        public DataChat? SendTo(CelesteNetPlayerSession? player, string text, string? tag = null, Color? color = null) {
            DataChat msg = new() {
                Target = player?.PlayerInfo,
                Text = text,
                Tag = tag ?? "",
                Color = color ?? Settings.ColorServer
            };
            if (player == null || msg.Target == null) {
                Logger.Log(LogLevel.INF, "chat", $"Sending to nobody: {text}");
                PrepareAndLog(null, msg);
                return null;
            }

            Logger.Log(LogLevel.INF, "chat", $"Sending to {msg.Target}: {text}");
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
