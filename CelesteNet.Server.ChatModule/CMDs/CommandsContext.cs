using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CommandsContext : IDisposable {

        public readonly List<ChatCmd> All = new();
        public readonly Dictionary<string, ChatCmd> ByID = new();
        public readonly Dictionary<Type, ChatCmd> ByType = new();
        public readonly DataCommandList DataAll = new DataCommandList();

        public CommandsContext(ChatModule chat) {
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(ChatCmd).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                ChatCmd? cmd = (ChatCmd?)Activator.CreateInstance(type);
                // TODO: We have a lot of things in the server that throw Exceptions to indicate we can't properly run,
                // we should perhaps convert those to Trace.Assert?
                Trace.Assert(cmd is not null, $"Cannot create instance of CMD {type.FullName}");
                Logger.Log(LogLevel.VVV, "chatcmds", $"Found command: {cmd!.ID.ToLowerInvariant()} ({type.FullName}, {cmd.Completion})");
                All.Add(cmd);
                ByID[cmd.ID.ToLowerInvariant()] = cmd;
                ByType[type] = cmd;
            }
            DataAll.List = new CommandInfo[All.Count];

            int i = 0;
            foreach (ChatCmd cmd in All) {
                cmd.Init(chat);

                ChatCmd? aliasTo = null;
                // check if **base** type is an existing command in ByType, which means this cmd is an alias
                // N.B. the base type ChatCmd itself is abstract and shouldn't be in ByType; see above
                Type? cmdBase = cmd.GetType().BaseType;
                if (cmdBase != null)
                    ByType.TryGetValue(cmdBase, out aliasTo);

                if (aliasTo != null)
                    Logger.Log(LogLevel.VVV, "chatcmds", $"Command: {cmd.ID.ToLowerInvariant()} is {(cmd.InternalAliasing ? "internal alias" : "alias")} of {aliasTo.ID.ToLowerInvariant()}");

                DataAll.List[i++] = new CommandInfo() {
                    ID = cmd.ID,
                    Auth = cmd.MustAuth,
                    AuthExec = cmd.MustAuthExec,
                    FirstArg = cmd.Completion,
                    AliasTo = cmd.InternalAliasing ? "" : aliasTo?.ID.ToLowerInvariant() ?? ""
                };
            }

            All = All.OrderBy(cmd => cmd.HelpOrder).ToList();
        }

        public void Dispose() {
            foreach (ChatCmd cmd in All)
                cmd.Dispose();
        }

        public ChatCmd? Get(string id)
            => ByID.TryGetValue(id, out ChatCmd cmd) ? cmd : null;

        public T? Get<T>(string id) where T : ChatCmd
            => ByID.TryGetValue(id, out ChatCmd cmd) ? (T)cmd : null;

        public T Get<T>() where T : ChatCmd
            => ByType.TryGetValue(typeof(T), out ChatCmd cmd) ? (T)cmd : throw new KeyNotFoundException($"Invalid CMD type {typeof(T).FullName}");

    }

    public abstract class ChatCmd : IDisposable {

        public static readonly char[] NameDelimiters = {
            ' ', '\n'
        };

#pragma warning disable CS8618 // Set manually after construction.
        public ChatModule Chat;
#pragma warning restore CS8618

        public List<ArgParser> ArgParsers = new();

        public virtual string ID => GetType().Name.Substring(3).ToLowerInvariant();

        public abstract string Info { get; }
        public virtual string Help => Info;
        public virtual int HelpOrder => 0;

        public virtual bool MustAuth => false;
        public virtual bool MustAuthExec => false;

        public virtual CompletionType Completion => CompletionType.None;

        public virtual bool InternalAliasing => false;

        public virtual void Init(ChatModule chat) {
            Chat = chat;
        }

        public virtual void Dispose() {
        }

        public virtual void ParseAndRun(CmdEnv env) {
            if (MustAuth && !env.IsAuthorized || MustAuthExec && !env.IsAuthorizedExec) {
                env.Error(new CommandRunException("Unauthorized!"));
                return;
            }

            List<Exception> caught = new();

            if (ArgParsers.Count == 0) {
                try {
                    ParseAndRun(env, null);
                    return;
                } catch (Exception e) {
                    Logger.Log(LogLevel.DEV, "ChatCMD", $"ParseAndRun exception caught: {e.Message} (no parsers)");
                    caught.Add(e);
                }
            }

            foreach (ArgParser parser in ArgParsers) {
                try {
                    ParseAndRun(env, parser);
                    return;
                } catch (Exception e) {
                    Logger.Log(LogLevel.DEV, "ChatCMD", $"ParseAndRun exception caught: {e.Message} ({ArgParsers.IndexOf(parser)})");
                    caught.Add(e);
                }
            }

            // We should only reach this point if something went wrong; otherwise we'd have returned already.

            if (caught.Count == 0) {
                env.Error(new CommandRunException("Could not parse command."));
                return;
            }

            int maxArgsParsed = 0, argParserExceptions = 0;
            foreach (Exception e in caught) {
                Logger.Log(LogLevel.VVV, "chatcmd", $"Caught exception: {e.GetType().Name} {e.Message}");
                if (e is not ArgParserException ape)
                    continue;
                Logger.Log(LogLevel.DEV, "chatcmd", $"(ArgParserException: {ape.paramsParsed} parsed, {ape.cmd}, {ape.args}, {ape.innerParam}");
                if (ape.paramsParsed >  maxArgsParsed)
                    maxArgsParsed = ape.paramsParsed;
                argParserExceptions++;
            }

            Logger.Log(LogLevel.DEV, "chatcmd", $"maxArgsParsed {maxArgsParsed}, parseExceptions {argParserExceptions}");

            // get rid of arg parser exceptions that got less far in parsing than another parser
            if (argParserExceptions > 1 && maxArgsParsed > 0)
                caught = caught.Where(e => e is not ArgParserException ape || ape.paramsParsed >= maxArgsParsed).ToList();

            // The reasoning here is that if there's any parsing exception that came from parsing a single param,
            // then other more 'generic' parser exceptions probably don't matter (e.g. number of parameters didn't match anyways)
            if (caught.Any(e => e is ArgParserException ape && ape.innerParam != null)) {
                caught = caught.Where(e => e is not ArgParserException ape || ape.innerParam != null).ToList();
            }

            IEnumerable<Exception> cmd_exceptions = caught.Where(e => CmdEnv.IsCmdException(e));
            IEnumerable<Exception> cmdrun_exceptions = caught.Where(e => e is CommandRunException);

            // if there are any of our custom exceptions, only report those to the player
            if (cmd_exceptions.Any()) {
                // if there were CommandRunException, only report that, ignore argparser/param exceptions?
                if (cmdrun_exceptions.Any())
                    caught = cmdrun_exceptions.ToList();
                else
                    caught = cmd_exceptions.ToList();
            }

            if (caught.Count > 0) {
                env.Errors(caught);
                return;
            }

            // just to make sure we always return at least a generic error when ParseAndRun failed. Other 'env.Error(s)' returned early.
            env.Error();
        }

        public virtual void ParseAndRun(CmdEnv env, ArgParser? parser) {
            string raw = env.FullText.Substring(Chat.Settings.CommandPrefix.Length + ID.Length);

            List<ICmdArg>? args = parser?.Parse(raw, env);

            Run(env, args);
        }

        public virtual void Run(CmdEnv env, List<ICmdArg>? args) {
        }

    }

    [Serializable]
    public class CommandRunException : Exception {

        public CommandRunException() { }

        public CommandRunException(string message)
            : base(message) { }

        public CommandRunException(string message, Exception inner)
            : base(message, inner) { }
    }

    public class CmdEnv {

        private readonly ChatModule Chat;
        public readonly DataChat Msg;

        public ChatCmd? Cmd;

        public CmdEnv(ChatModule chat, DataChat msg) {
            Chat = chat;
            Msg = msg;
        }

        public uint PlayerID => Msg.Player?.ID ?? uint.MaxValue;

        public CelesteNetServer Server => Chat.Server ?? throw new Exception("Not ready.");

        public static bool IsParsingException(Exception e) => e is ArgParserException || e is ParamException;
        public static bool IsCmdException(Exception e) => e is ArgParserException || e is ParamException || e is CommandRunException;

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

        public bool IsAuthorized => !(Session?.UID?.IsNullOrEmpty() ?? true) && Chat.Server.UserData.TryLoad(Session.UID, out BasicUserInfo info) && (info.Tags.Contains(BasicUserInfo.TAG_AUTH) || info.Tags.Contains(BasicUserInfo.TAG_AUTH_EXEC));
        public bool IsAuthorizedExec => !(Session?.UID?.IsNullOrEmpty() ?? true) && Chat.Server.UserData.TryLoad(Session.UID, out BasicUserInfo info) && info.Tags.Contains(BasicUserInfo.TAG_AUTH_EXEC);

        public string FullText => Msg.Text;
        public string Text => Cmd == null ? Msg.Text : Msg.Text.Substring(Chat.Settings.CommandPrefix.Length + Cmd.ID.Length);

        public DataChat? Send(string text, string? tag = null, Color? color = null) => Chat.SendTo(Session, text, tag, color ?? Chat.Settings.ColorCommandReply);

        public DataChat? Error(Exception? e = null) {
            string cmdName = Cmd?.ID ?? "?";

            if (e != null && IsCmdException(e)) {
                Logger.Log(LogLevel.VVV, "chatcmd", $"Command {cmdName} failed:\n{e}");
                return Send($"Command {cmdName} failed: {e.Message}", color: Chat.Settings.ColorError);
            }

            Logger.Log(LogLevel.ERR, "chatcmd", $"Command {cmdName} failed:\n{e}");
            return Send($"Command {cmdName} failed due to an internal error.", color: Chat.Settings.ColorError);
        }

        public DataChat? Errors(List<Exception> errors) {
            if (errors.Count == 1)
                return Error(errors[0]);

            string cmdName = Cmd?.ID ?? "?";
            StringBuilder errorListing = new();

            try {
                foreach (Exception e in errors) {
                    errorListing
                        .AppendLine()
                        .Append(" - ")
                        .Append(e.Message);
                }
            } catch (Exception e) {
                Error(e);
            }

            return Send($"Command {cmdName} failed:{errorListing}", color: Chat.Settings.ColorError);
        }
    }

}
