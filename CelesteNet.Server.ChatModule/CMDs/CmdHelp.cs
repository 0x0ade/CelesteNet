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
            parser.AddParameter(new ParamString(chat), "command", "join");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg>? args) {
            if (args?.Count > 0) {

                string helpOutput = args[0] switch {
                    CmdArgInt getPageNum    => GetCommandPage(env, getPageNum.Int),
                    CmdArgString cmdNameArg => GetCommandSnippet(env, cmdNameArg.String),
                    _ => GetCommandSnippet(env, args[0].ToString() ?? ""),
                };

                env.Send(helpOutput);
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
                    // for commands with multiple parsers with single arguments, list them with "|" on one line
                    builder
                        .Append(prefix)
                        .Append(cmd.ID)
                        .Append(" ");

                    foreach (ArgParser args in parsers) {
                        builder
                            .Append(args.ToString())
                            .Append(args == parsers.Last() ? "" : " | ");
                        // I almost rewrote this loop to be a do/while with MoveNext on an Enumerator
                        // but then I felt rather silly for trying to optimize away this call to Last()
                        // on a list enumerable that most likely has one or two elements. - Red
                    }

                    builder.AppendLine();
                } else if (cmd.ArgParsers.Count >= 1) {
                    // otherwise, list all the parsers on separate lines
                    foreach (ArgParser args in parsers) {
                        builder
                            .Append(prefix)
                            .Append(cmd.ID)
                            .Append(" ")
                            .Append(args.ToString())
                            .AppendLine();
                    }
                } else {
                    // or just this when there's not even ArgParsers
                    builder
                        .Append(prefix)
                        .Append(cmd.ID)
                        .AppendLine();
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
            if (cmdName.IsNullOrEmpty())
                throw new CommandRunException($"Command not found.");

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
                builderExamples
                    .Append(prefix)
                    .Append(cmd.ID)
                    .Append(" ")
                    .Append(args.ToExample())
                    .AppendLine();
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
