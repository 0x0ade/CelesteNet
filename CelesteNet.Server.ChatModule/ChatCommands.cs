using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatCommands : IDisposable {

        public readonly List<ChatCMD> All = new();
        public readonly Dictionary<string, ChatCMD> ByID = new();
        public readonly Dictionary<Type, ChatCMD> ByType = new();

        public ChatCommands(ChatModule chat) {
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(ChatCMD).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                ChatCMD? cmd = (ChatCMD?) Activator.CreateInstance(type);
                if (cmd == null)
                    throw new Exception($"Cannot create instance of CMD {type.FullName}");
                Logger.Log(LogLevel.VVV, "chatcmds", $"Found command: {cmd.ID.ToLowerInvariant()} ({type.FullName})");
                All.Add(cmd);
                ByID[cmd.ID.ToLowerInvariant()] = cmd;
                ByType[type] = cmd;
            }

            foreach (ChatCMD cmd in All)
                cmd.Init(chat);

            All = All.OrderBy(cmd => cmd.HelpOrder).ToList();
        }

        public void Dispose() {
            foreach (ChatCMD cmd in All)
                cmd.Dispose();
        }

        public ChatCMD? Get(string id)
            => ByID.TryGetValue(id, out ChatCMD? cmd) ? cmd : null;

        public T? Get<T>(string id) where T : ChatCMD
            => ByID.TryGetValue(id, out ChatCMD? cmd) ? (T) cmd : null;

        public T Get<T>() where T : ChatCMD
            => ByType.TryGetValue(typeof(T), out ChatCMD? cmd) ? (T) cmd : throw new Exception($"Invalid CMD type {typeof(T).FullName}");

    }

    public abstract class ChatCMD : IDisposable {

        public static readonly char[] NameDelimiters = {
            ' ', '\n'
        };

#pragma warning disable CS8618 // Set manually after construction.
        public ChatModule Chat;
#pragma warning restore CS8618
        public virtual string ID => GetType().Name.Substring(7).ToLowerInvariant();

        public abstract string Args { get; }
        public abstract string Info { get; }
        public virtual string Help => Info;
        public virtual int HelpOrder => 0;

        public virtual void Init(ChatModule chat) {
            Chat = chat;
        }

        public virtual void Dispose() {
        }

        public virtual void ParseAndRun(ChatCMDEnv env) {
            // TODO: Improve or rewrite. This comes from GhostNet, which adopted it from disbot (0x0ade's C# Discord bot).

            string raw = env.FullText;

            int index = Chat.Settings.CommandPrefix.Length + ID.Length - 1; // - 1 because next space required
            List<ChatCMDArg> args = new();
            while (
                index + 1 < raw.Length &&
                (index = raw.IndexOf(' ', index + 1)) >= 0
            ) {
                int next = index + 1 < raw.Length ? raw.IndexOf(' ', index + 1) : -2;
                if (next < 0)
                    next = raw.Length;

                int argIndex = index + 1;
                int argLength = next - index - 1;

                // + 1 because space
                args.Add(new ChatCMDArg(env).Parse(raw, argIndex, argLength));

                // Parse a split up range (with spaces) into a single range arg
                if (args.Count >= 3 &&
                    args[args.Count - 3].Type == ChatCMDArgType.Int &&
                    (args[args.Count - 2].String == "-" || args[args.Count - 2].String == "+") &&
                    args[args.Count - 1].Type == ChatCMDArgType.Int
                ) {
                    args.Add(new ChatCMDArg(env).Parse(raw, args[args.Count - 3].Index, next - args[args.Count - 3].Index));
                    args.RemoveRange(args.Count - 4, 3);
                    continue;
                }
            }

            Run(env, args);
        }

        public virtual void Run(ChatCMDEnv env, List<ChatCMDArg> args) {
        }

    }

    public class ChatCMDArg {

        public ChatCMDEnv Env;

        public string RawText = "";
        public string String = "";
        public int Index;

        public ChatCMDArgType Type;

        public int Int;
        public long Long;
        public ulong ULong;
        public float Float;

        public int IntRangeFrom;
        public int IntRangeTo;
        public int IntRangeMin => Math.Min(IntRangeFrom, IntRangeTo);
        public int IntRangeMax => Math.Max(IntRangeFrom, IntRangeTo);

        public CelesteNetPlayerSession? Session {
            get {
                if (Type == ChatCMDArgType.Int || Type == ChatCMDArgType.Long) {
                    if (Env.Chat.Server.PlayersByID.TryGetValue((uint) Long, out CelesteNetPlayerSession? session))
                        return session;
                }

                using (Env.Chat.Server.ConLock.R())
                    return
                        Env.Chat.Server.Sessions.FirstOrDefault(session => session.PlayerInfo?.FullName == String) ??
                        Env.Chat.Server.Sessions.FirstOrDefault(session => session.PlayerInfo?.FullName.StartsWith(String, StringComparison.InvariantCultureIgnoreCase) ?? false);
            }
        }

        public ChatCMDArg(ChatCMDEnv env) {
            Env = env;
        }

        public virtual ChatCMDArg Parse(string raw, int index) {
            RawText = raw;
            if (index < 0 || raw.Length <= index) {
                String = "";
                Index = 0;
                return this;
            }
            String = raw.Substring(index);
            Index = index;

            return Parse();
        }
        public virtual ChatCMDArg Parse(string raw, int index, int length) {
            RawText = raw;
            String = raw.Substring(index, length);
            Index = index;

            return Parse();
        }

        public virtual ChatCMDArg Parse() {
            // TODO: Improve or rewrite. This comes from GhostNet, which adopted it from disbot (0x0ade's C# Discord bot).

            if (int.TryParse(String, out Int)) {
                Type = ChatCMDArgType.Int;
                Long = IntRangeFrom = IntRangeTo = Int;
                ULong = (ulong) Int;

            } else if (long.TryParse(String, out Long)) {
                Type = ChatCMDArgType.Long;
                ULong = (ulong) Long;

            } else if (ulong.TryParse(String, out ULong)) {
                Type = ChatCMDArgType.ULong;

            } else if (float.TryParse(String, out Float)) {
                Type = ChatCMDArgType.Float;
            }

            if (Type == ChatCMDArgType.String) {
                string[] split;
                int from, to;
                if ((split = String.Split('-')).Length == 2) {
                    if (int.TryParse(split[0].Trim(), out from) && int.TryParse(split[1].Trim(), out to)) {
                        Type = ChatCMDArgType.IntRange;
                        IntRangeFrom = from;
                        IntRangeTo = to;
                    }
                } else if ((split = String.Split('+')).Length == 2) {
                    if (int.TryParse(split[0].Trim(), out from) && int.TryParse(split[1].Trim(), out to)) {
                        Type = ChatCMDArgType.IntRange;
                        IntRangeFrom = from;
                        IntRangeTo = from + to;
                    }
                }
            }

            return this;
        }

        public string Rest => RawText.Substring(Index);

        public override string ToString() => String;

        public static implicit operator string(ChatCMDArg arg) => arg.String;

    }

    public enum ChatCMDArgType {
        String,

        Int,
        IntRange,

        Long,
        ULong,

        Float,
    }

    public class ChatCMDEnv {

        public readonly ChatModule Chat;
        public readonly DataChat Msg;

        public ChatCMD? Cmd;

        public ChatCMDEnv(ChatModule chat, DataChat msg) {
            Chat = chat;
            Msg = msg;
        }

        public uint PlayerID => Msg.Player?.ID ?? uint.MaxValue;

        public CelesteNetServer Server => Chat.Server ?? throw new Exception("Not ready.");

        public CelesteNetPlayerSession? Session {
            get {
                if (Msg.Player == null)
                    return null;
                if (!Chat.Server.PlayersByID.TryGetValue(PlayerID, out CelesteNetPlayerSession? session))
                    return null;
                return session;
            }
        }

        public DataPlayerInfo? Player => Session?.PlayerInfo;

        public DataPlayerState? State => Chat.Server.Data.TryGetBoundRef(Player, out DataPlayerState? state) ? state : null;

        public string FullText => Msg.Text;
        public string Text => Cmd == null ? Msg.Text : Msg.Text.Substring(Chat.Settings.CommandPrefix.Length + Cmd.ID.Length);

        public DataChat? Send(string text, string? tag = null, Color? color = null) => Chat.SendTo(Session, text, tag, color ?? Chat.Settings.ColorCommandReply);

        public DataChat? Error(Exception e) {
            string cmdName = Cmd?.ID ?? "?";

            if (e.GetType() == typeof(Exception)) {
                Logger.Log(LogLevel.VVV, "chatcmd", $"Command {cmdName} failed:\n{e}");
                return Send($"Command {cmdName} failed: {e.Message}", color: Chat.Settings.ColorError);
            }

            Logger.Log(LogLevel.ERR, "chatcmd", $"Command {cmdName} failed:\n{e}");
            return Send($"Command {cmdName} failed due to an internal error.", color: Chat.Settings.ColorError);
        }

    }

}
