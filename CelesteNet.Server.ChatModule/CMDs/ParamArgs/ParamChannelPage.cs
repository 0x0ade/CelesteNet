using System;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class ParamChannelPage : Param {

        public override string Help => "Page number of channel list";
        public override string PlaceholderName { get; set; } = "page";
        protected override string ExampleValue => "1";

        public ParamChannelPage(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None) : base(chat, validate, flags) {
        }

        public override bool TryParse(string raw, CmdEnv env, out ICmdArg? arg) {

            Channels channels = env.Server.Channels;

            bool parseSuccess = int.TryParse(raw, out int page);
            if (parseSuccess || string.IsNullOrWhiteSpace(raw)) {

                if (channels.All.Count == 0) {
                    // NB: No point in getting ChatCMDChannel.ID since that's not static - you could
                    // call Chat.Commands.Get("channel").ID or Chat.Commands.Get<ChatCMDChannel>().ID
                    // but that's just a longer way of spelling it out :)
                    throw new ParamException($"No channels. See {Chat.Settings.CommandPrefix}channel on how to create one.");
                }

                if (parseSuccess)
                    page--;

                int pages = (int)Math.Ceiling(channels.All.Count / (float)CmdChannel.pageSize);
                if (page < 0 || pages <= page)
                    throw new ParamException("Page out of range.");

                arg = new CmdArgChannelPage(page, channels.All.ToSnapshot());
                Validate?.Invoke(raw, env, arg);
                return true;
            }

            throw new ParamException("Invalid page number.");
        }
    }

    public class CmdArgChannelPage : CmdArgInt {

        public ListSnapshot<Channel>? ChannelList { get; }

        public CmdArgChannelPage(int val, ListSnapshot<Channel> channels) : base(val) {
            ChannelList = channels;
        }

        public override string ToString() => ChannelList != null ? ChannelList.ToString() ?? "" : Int.ToString();
    }
}
