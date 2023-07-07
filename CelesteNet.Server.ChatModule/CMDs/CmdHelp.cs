using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {

    public class CmdH : CmdHelp {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdHelp>().ID}";


    }

    public class CmdHelp : ChatCmd {

        public override CompletionType Completion => CompletionType.Command;

        public override string Info => "Get help on how to use commands.";

        public override int HelpOrder => int.MinValue;

        public override string Help =>
$@"List all commands with {Chat.Settings.CommandPrefix}{ID} [page number]
Show help on a command with {Chat.Settings.CommandPrefix}{ID} <cmd>
(You did exactly that just now to get here, I assume!)";

        public const int pageSize = 8;

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamHelpPage(chat, null, ParamFlags.Optional));
            parser.HelpOrder = int.MinValue;
            ArgParsers.Add(parser);

            parser = new(chat, this);
            parser.AddParameter(new ParamString(chat, null, ParamFlags.None, 0, @"[^0-9]+"), "command", "join");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg> args) {
            Logger.Log(LogLevel.DEV, "cmdhelp", $"{GetType()}.Run args# {args?.Count}");
            if (args?.Count > 0) {
                Logger.Log(LogLevel.DEV, "cmdhelp", $"{GetType()}.Run arg0: {args[0]}");
                if (args[0] is CmdArgInt getPageNum) {
                    Logger.Log(LogLevel.DEV, "cmdhelp", $"{GetType()}.Run arg0 is int: {getPageNum.Int}");
                    env.Send(GetCommandPage(env, getPageNum.Int));
                } else if (args[0] is CmdArgString cmdNameArg) {
                    env.Send(GetCommandSnippet(env, cmdNameArg.String));
                } else {
                    env.Send(GetCommandSnippet(env, args[0].ToString()));
                }

                return;
            }

            Logger.Log(LogLevel.DEV, "cmdhelp", $"Sending zero page");
            env.Send(GetCommandPage(env));
        }

       public string GetCommandPage(CmdEnv env, int page = 0) {

            string prefix = Chat.Settings.CommandPrefix;
            StringBuilder builder = new();

            List<ChatCmd> all = Chat.Commands.All.Where(cmd =>
                env.IsAuthorizedExec || (!cmd.MustAuthExec && (
                    env.IsAuthorized || !cmd.MustAuth
                ))
            ).ToList();

            int pages = (int) Math.Ceiling(all.Count / (float) pageSize);
            if (page < 0 || pages <= page)
                throw new CommandRunException("Page out of range.");

            for (int i = page * pageSize; i < (page + 1) * pageSize && i < all.Count; i++) {
                ChatCmd cmd = all[i];

                IOrderedEnumerable<ArgParser> parsers = cmd.ArgParsers.OrderBy(ap => ap.HelpOrder);

                if (cmd.ArgParsers.Count > 1 && cmd.ArgParsers.TrueForAll(ap => ap.Parameters.Count == 1)) {
                    StringBuilder alternatives = new();

                    alternatives
                            .Append(prefix)
                            .Append(cmd.ID)
                            .Append(" ");
                    foreach (ArgParser args in parsers) {
                        alternatives
                            .Append(args.ToString())
                            .Append(args == parsers.Last() ? "" : " | ");
                    }
                    builder
                        .Append(alternatives.ToString())
                        .AppendLine();
                } else {
                    foreach (ArgParser args in parsers) {
                        builder
                            .Append(prefix)
                            .Append(cmd.ID)
                            .Append(" ")
                            .Append(args.ToString())
                            .AppendLine();
                    }
                }
            }

            builder
                .Append("Page ")
                .Append(page + 1)
                .Append("/")
                .Append(pages);

            return builder.ToString().Trim();
        }

        public string GetCommandSnippet(CmdEnv env, string cmdName) {
            ChatCmd? cmd = Chat.Commands.Get(cmdName.TrimStart('/'));
            if (cmd == null)
                throw new CommandRunException($"Command {cmdName} not found.");

            if ((cmd.MustAuth && !env.IsAuthorized) || (cmd.MustAuthExec && !env.IsAuthorizedExec))
                throw new CommandRunException("Unauthorized!");

            return Help_GetCommandSnippet(cmd);
        }

        public string Help_GetCommandSnippet(ChatCmd cmd) {
            string prefix = Chat.Settings.CommandPrefix;
            StringBuilder builder = new();
            StringBuilder builderSyntax = new();
            StringBuilder builderExamples = new();

            IOrderedEnumerable<ArgParser> parsers = cmd.ArgParsers.OrderBy(ap => ap.HelpOrder);
            foreach (ArgParser args in parsers) {
                builderSyntax
                    .Append(prefix)
                    .Append(cmd.ID)
                    .Append(" ")
                    .Append(args.ToString())
                    .AppendLine();
                //if (parsers.Any(ap => ap.NeededParamCount > 0)) {
                    builderExamples
                        .Append(prefix)
                        .Append(cmd.ID)
                        .Append(" ")
                        .Append(args.ToExample())
                        .AppendLine();
                //}
            }

            if (cmd.ArgParsers.Count == 0) {
                builderSyntax
                    .Append(prefix)
                    .Append(cmd.ID)
                    .AppendLine();
            }

            builder
                .Append(builderSyntax.ToString())
                .AppendLine()
                .AppendLine(cmd.Help);


            if (builderExamples.Length > 0) {
                builder
                    .AppendLine()
                    .AppendLine("Examples:")
                    .Append(builderExamples.ToString());
            }

            return builder.ToString().Trim();
        }

    }
}
