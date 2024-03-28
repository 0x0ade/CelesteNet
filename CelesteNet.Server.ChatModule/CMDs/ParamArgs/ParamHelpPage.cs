using System;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class ParamHelpPage : Param {

        public override string Help => "Page number of command list";
        public override string PlaceholderName { get; set; } = "page";
        protected override string ExampleValue => "1";

        public ParamHelpPage(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None) : base(chat, validate, flags) {
        }

        public override bool TryParse(string raw, CmdEnv env, out ICmdArg? arg) {

            bool parseSuccess = int.TryParse(raw, out int page);
            if (parseSuccess || string.IsNullOrWhiteSpace(raw)) {

                if (parseSuccess)
                    page--;

                Logger.Log(LogLevel.DEV, "paramhelppage", $"{GetType()}.TryParse is int: {page}");

                int pages = (int)Math.Ceiling(Chat.Commands.All.Count / (float)CmdHelp.pageSize);
                if (page < 0 || pages <= page)
                    throw new ParamException("Page out of range.");

                arg = new CmdArgInt(page);
                Validate?.Invoke(raw, env, arg);
                return true;
            }

            throw new ParamException("Invalid page number.");
        }
    }
}
