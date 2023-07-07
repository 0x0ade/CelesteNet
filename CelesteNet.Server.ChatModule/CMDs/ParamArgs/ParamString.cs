using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class ParamString : Param {
        public override string Help => "A string" + (maxLength > 0 ? $" (max. {maxLength} characters)" : "");
        public override string PlaceholderName { get; set; } = "string";
        protected override string ExampleValue => "Text";
        public int maxLength;
        public Regex? Re;
        public bool Truncate;

        public ParamString(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None,
                              int maxlength = 0, string? regex = null, bool truncate = true) : base(chat, validate, flags) {
            maxLength = maxlength;
            if (!regex.IsNullOrEmpty())
                Re = new Regex(regex, RegexOptions.Compiled | RegexOptions.ExplicitCapture);
            Truncate = truncate;
        }

        public override bool TryParse(string raw, CmdEnv env, out ICmdArg? arg) {
            if (maxLength > 0 && raw.Length > maxLength) {
                if (Truncate) {
                    raw = raw.Substring(0, maxLength);
                } else {
                    arg = null;
                    return false;
                }
            }

            if (Re != null) {
                Logger.Log(LogLevel.DEV, "paramstring", $"Testing '{raw}' against regex {Re.ToString()} is {Re.IsMatch(raw)}");
            }

            if (!Re?.IsMatch(raw) ?? false)
                throw new ParamException($"Argument '{raw}' is invalid.");

            arg = new CmdArgString(raw);
            Validate?.Invoke(raw, env, arg);
            return true;
        }
    }

    public class CmdArgString : ICmdArg {
        public string String { get; }

        public CmdArgString(string val) {
            String = val;
        }

        public override string ToString() => String;

        public static implicit operator string(CmdArgString arg) => arg.String;
    }
}
