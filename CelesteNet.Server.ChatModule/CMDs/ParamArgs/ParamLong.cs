using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class ParamLong : Param {
        public override string Help => "A long (integer) value";
        public override string PlaceholderName { get; set; } = "long";
        protected override string ExampleValue => Max.ToString();
        public long Min, Max;

        public ParamLong(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None,
                              long min = long.MinValue, long max = long.MaxValue) : base(chat, validate, flags) {
            Min = Math.Max(min, Flags.HasFlag(ParamFlags.NonNegative) ? Flags.HasFlag(ParamFlags.NonZero) ? 1 : 0 : long.MinValue);
            Max = Math.Min(max, Flags.HasFlag(ParamFlags.NonPositive) ? Flags.HasFlag(ParamFlags.NonZero) ? -1 : 0 : long.MaxValue);
        }

        public override bool TryParse(string raw, CmdEnv env, out ICmdArg? arg) {

            if (long.TryParse(raw, out long result) && result >= Min && result <= Max) {
                arg = new CmdArgLong(result);
                Validate?.Invoke(raw, env, arg);
                return true;
            }

            arg = null;
            return false;
        }
    }

    public class CmdArgLong : ICmdArg {

        public long Long { get; }

        public CmdArgLong(long val) {
            Long = val;
        }

        public override string ToString() => Long.ToString();
    }
}
