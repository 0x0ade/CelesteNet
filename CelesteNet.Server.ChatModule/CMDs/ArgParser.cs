using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class ArgParser {
        public readonly ChatModule Chat;
        public readonly ChatCmd Cmd;

        public static readonly string[] paramPosHumanize =
        {
            "first", "second", "third", "fourth", "fifth"
        };

        public bool AllOptional => Parameters.TrueForAll((Param a) => { return a.isOptional; });
        public int NeededParamCount => Parameters.Count(p => !p.isOptional);
        public bool NoParse => Parameters.Count == 0;
        public bool IgnoreExtra = true;
        public int HelpOrder = 0;

        // Only accepts one ArgType parser per position,
        // so if multiple ArgTypes apply you will need multiple ArgParsers.
        // Otherwise e.g. modeling a command that takes either
        // 2 Ints or 2 Strings but not an Int + String would be kinda hard?
        public List<Param> Parameters;

        public char[] Delimiters;

        public ArgParser(ChatModule chat, ChatCmd cmd, char[]? delimiters = null) {
            Chat = chat;
            Cmd = cmd;
            Parameters = new();
            Delimiters = delimiters ?? new char[] { ' ' };
        }

        public void AddParameter(Param p, string? placeholder = null, string? customExample = null) {
            Param prev;
            if (!p.isOptional && Parameters.Count > 0 && (prev = Parameters.Last()).isOptional)
                throw new Exception($"Parameter {p.Help} of {Cmd.ID} must be Optional after {prev.Help} (flags = {prev.Flags})");

            if (!placeholder.IsNullOrEmpty()) {
                p.PlaceholderName = placeholder;
            }

            if (!customExample.IsNullOrEmpty()) {
                p.CustomExample = customExample;
            }

            Parameters.Add(p);
        }

        public List<ICmdArg> Parse(string raw, CmdEnv env) {
            List<ICmdArg> values = new();

            raw = raw.Trim();

            Logger.Log(LogLevel.DEV, "argparse", $"Running '{raw}' through parser with {Parameters.Count} / np: {NoParse} / ie: {IgnoreExtra}");

            if (raw.IsNullOrEmpty() && AllOptional)
                return values;

            if (NoParse) {
                if (!IgnoreExtra && !raw.IsNullOrEmpty()) {
                    throw new ArgParserException($"Too many parameters given: '{raw}'.", this);
                }
                values.Add(new CmdArgString(raw));
                return values;
            }

            Logger.Log(LogLevel.DEV, "argparse", $"Parsing '{raw}'...");

            string extraArguments = "";

            int from, to = -1, p;
            for (p = 0; p <= Parameters.Count; p++) {
                Param? param = null;

                if (p < Parameters.Count) {
                    param = Parameters[p];
                }

                if ((from = to + 1) >= raw.Length) {
                    Logger.Log(LogLevel.DEV, $"argparse{p}", $"End of 'raw' reached at {from} on param {p} ({param}).");
                    break;
                }

                if ((to = raw.IndexOfAny(Delimiters, from)) < 0
                    || (param is ParamString && p == Parameters.Count - 1)) {
                    to = raw.Length;
                }

                int argIndex = from;
                int argLength = to - from;

                string rawValue = raw.Substring(argIndex, argLength).Trim();

                Logger.Log(LogLevel.DEV, $"argparse{p}", $"Looking for param {p} {param?.Placeholder} at {argIndex}+{argLength}.");
                Logger.Log(LogLevel.DEV, $"argparse{p}", $"Substring is '{rawValue}'.");

                if (p == Parameters.Count || param == null) {
                    extraArguments = rawValue;
                    break;
                }

                // detect spaced out ranges
                if (param is ParamIntRange && raw[to] == ' ') {
                    int lookahead_idx = to + 1;
                    while (lookahead_idx < raw.Length && raw[lookahead_idx] == ' ')
                        lookahead_idx++;

                    if (raw[lookahead_idx] == '-' && raw[++lookahead_idx] == ' ') {
                        while (lookahead_idx < raw.Length && raw[lookahead_idx] == ' ')
                            lookahead_idx++;
                        if ((to = raw.IndexOf(' ', lookahead_idx)) < 0) {
                            to = raw.Length;
                        }
                        rawValue = raw.Substring(argIndex, to - from - 1).Replace(" ", "");
                    }
                }

                try {
                    if (param.TryParse(rawValue, env, out ICmdArg? arg) && arg != null) {
                        Logger.Log(LogLevel.DEV, $"argparse{p}", $"{param.GetType()}.TryParse returned {arg.GetType().FullName}.");
                        values.Add(arg);
                    } else {
                        Logger.Log(LogLevel.DEV, $"argparse{p}", $"{param.GetType()}.TryParse failed or returned null.");
                        throw new ArgParserException($"Failed to parse '{rawValue}' as {Parameters[p].Placeholder}.", this, parsed: p);
                    }
                } catch (ParamException pe) {
                    throw new ArgParserException($"{pe.Message} (parameter '{Parameters[p].Placeholder}')", this, pe, p);
                }
            }

            if (raw.IsNullOrEmpty() && Parameters.Count > 0 && !Parameters[0].isOptional) {
                Logger.Log(LogLevel.DEV, $"argparse{p}", $"raw IsNullOrEmpty and there's non-optional first param...");
                if (Parameters[0].TryParse(raw, env, out ICmdArg? arg) && arg != null) {
                    values.Add(arg);
                    p = 1;
                }
            }

            if (p == Parameters.Count && !extraArguments.IsNullOrEmpty() && !IgnoreExtra)
                throw new ArgParserException($"Too many parameters given: '{extraArguments}'.", this, parsed: p);

            if (p < Parameters.Count && !Parameters[p].isOptional)
                throw new ArgParserException($"Necessary parameter {Parameters[p].Placeholder} not found.", this, parsed: p);

            return values;
        }

        public string ToExample() => string.Join(" ", Parameters.Select(p => p.Example));

        public override string ToString() => string.Join(" ", Parameters.Select(p => p.Placeholder));
    }

    [Serializable]
    public class ArgParserException : Exception {

        public readonly string cmd = "", args = "";
        public readonly ParamException? innerParam;
        public readonly int paramsParsed = 0;

        public ArgParserException() {
        }

        public ArgParserException(string message)
            : base(message) {
        }

        public ArgParserException(string message, Exception inner)
            : base(message, inner) {
        }

        public ArgParserException(string message, ArgParser ap, ParamException? pe = null, int parsed = 0)
            : base(message) {
            cmd = ap.Cmd.ID;
            args = ap.ToString();
            innerParam = pe;
            paramsParsed = parsed;
        }
    }
}
