using System;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {

    public class ParamInt : Param {
        public override string Help => "An integer value";
        public override string PlaceholderName { get; set; } = "int";
        protected override string ExampleValue => $"{(Min == int.MinValue ? -999 : Min + (Max == int.MaxValue ? 9999 : Max) / 2):D}";
        public int Min, Max;

        public ParamInt(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None,
                              int min = int.MinValue, int max = int.MaxValue) : base(chat, validate, flags) {
            Min = Math.Max(min, Flags.HasFlag(ParamFlags.NonNegative) ? Flags.HasFlag(ParamFlags.NonZero) ? 1 : 0 : int.MinValue);
            Max = Math.Min(max, Flags.HasFlag(ParamFlags.NonPositive) ? Flags.HasFlag(ParamFlags.NonZero) ? -1 : 0 : int.MaxValue);
        }

        public override bool TryParse(string raw, CmdEnv env, out ICmdArg? arg) {

            if (int.TryParse(raw, out int result) && result >= Min && result <= Max) {
                arg = new CmdArgInt(result);
                Validate?.Invoke(raw, env, arg);
                return true;
            }

            arg = null;
            return false;
        }
    }

    public class CmdArgInt : ICmdArg {

        public int Int { get; }

        public CmdArgInt(int val) {
            Int = val;
        }

        public override string ToString() => Int.ToString();
    }
}
