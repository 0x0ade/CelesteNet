using System;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class ParamChannelName : Param {

        public override string Help => $"Name of {(MustExist ? "an existing" : "an existing or new")} channel";
        public override string PlaceholderName { get; set; } = "channel";
        protected override string ExampleValue => "main";

        public bool MustExist = false;

        public ParamChannelName(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None,
                              bool mustExist = false) : base(chat, validate, flags) {
            MustExist = mustExist;
        }

        public override bool TryParse(string raw, CmdEnv env, out ICmdArg? arg) {

            CelesteNetPlayerSession? self = env.Session;

            if (MustExist) {
                if (env.Server.Channels.ByName.TryGetValue(raw.Trim(), out Channel? channel)) {
                    arg = new CmdArgChannelName(raw, channel);
                } else {
                    throw new ParamException($"Channel {raw} not found.");
                }
            } else {
                if (int.TryParse(raw, out int _))
                    throw new ParamException("Invalid channel name.");
                arg = new CmdArgChannelName(raw, null);
            }

            Validate?.Invoke(raw, env, arg);
            return true;
        }
    }

    public class CmdArgChannelName : ICmdArg {

        public Channel? Channel { get; }
        public string Name { get; }

        public CmdArgChannelName(string name, Channel? channel) {
            Name = name;
            Channel = channel;
        }

        public override string ToString() => Channel?.Name ?? Name;
    }
}
