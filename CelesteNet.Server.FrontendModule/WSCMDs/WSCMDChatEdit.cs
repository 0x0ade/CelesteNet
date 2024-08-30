using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDChatEdit : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            if (input == null)
                return null;

            ChatModule chat = Frontend.Server.Get<ChatModule>();
            if (!chat.ChatLog.TryGetValue((uint?) input.ID ?? uint.MaxValue, out DataChat? msg))
                return null;

            if (input.Color != null)
                msg.Color = ColorHelpers.HexToColor((string) input.Color);

            if (input.Text != null)
                msg.Text = (string) input.Text;

            chat.ForceSend(msg);
            return msg.ToDetailedFrontendChat();
        }
    }
}
