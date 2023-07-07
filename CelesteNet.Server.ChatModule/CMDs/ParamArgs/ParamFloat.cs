using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class ParamFloat : Param {

        public override string Help => "A floating-point value";
        public override string PlaceholderName { get; set; } = "float";
        public float Min, Max;

        public ParamFloat(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None,
                              float min = float.MinValue, float max = float.MaxValue) : base(chat, validate, flags) {
            Min = Math.Max(min, Flags.HasFlag(ParamFlags.NonNegative) ? Flags.HasFlag(ParamFlags.NonZero) ? 1 : 0 : float.MinValue);
            Max = Math.Min(max, Flags.HasFlag(ParamFlags.NonPositive) ? Flags.HasFlag(ParamFlags.NonZero) ? -1 : 0 : float.MaxValue);
        }

        public override bool TryParse(string raw, CmdEnv env, out ICmdArg? arg) {

            if (float.TryParse(raw, out float result) && result >= Min && result <= Max) {
                arg = new CmdArgFloat(result);
                Validate?.Invoke(raw, env, arg);
                return true;
            }

            arg = null;
            return false;
        }
    }

    public class CmdArgFloat : ICmdArg {

        public float Float { get; }

        public CmdArgFloat(float val) {
            Float = val;
        }

        public override string ToString() => Float.ToString();
    }
}
