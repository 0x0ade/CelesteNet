using System;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public abstract class Param {

        public readonly ChatModule Chat;

        public virtual string Help => "";
        public virtual string PlaceholderName { get; set; } = "?";
        public virtual string Placeholder {
            get {
                if (PlaceholderName.Length > 1)
                    if (PlaceholderName[0] == '<' || PlaceholderName[0] == '[')
                        return PlaceholderName;

                if (Flags.HasFlag(ParamFlags.Optional))
                    return "[" + PlaceholderName + "]";
                else
                    return "<" + PlaceholderName + ">";
            }
        }

        public virtual string Example => !CustomExample.IsNullOrEmpty() ? CustomExample : ExampleValue;

        protected virtual string ExampleValue => "?";
        public string CustomExample { get; set; } = "";

        public readonly ParamFlags Flags = ParamFlags.None;
        public bool isOptional => Flags.HasFlag(ParamFlags.Optional);
        public Action<string, CmdEnv, ICmdArg>? Validate;

        protected Param(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None) {
            Chat = chat;
            Validate = validate;
            Flags = flags;
        }

        public abstract bool TryParse(string raw, CmdEnv env, out ICmdArg? arg);

        public override string ToString() => Help;

        public static implicit operator string(Param arg) => arg.Help;

    }

    [Serializable]
    public class ParamException : Exception {

        public ParamException() {
        }

        public ParamException(string message)
            : base(message) {
        }

        public ParamException(string message, Exception inner)
            : base(message, inner) {
        }
    }

    public interface ICmdArg {
    }

    [Flags]
    public enum ParamFlags {
        None,
        Optional,
        NonPositive,
        NonNegative,
        NonZero
    }
}
