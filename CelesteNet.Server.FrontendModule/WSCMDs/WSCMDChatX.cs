using Celeste.Mod.CelesteNet.MonocleCelesteHelpers;
using Celeste.Mod.CelesteNet.Server.Chat;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDChatX : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            if (input == null)
                return null;

            ChatModule chat = Frontend.Server.Get<ChatModule>();

            string text = (string?) input.Text ?? "";
            string tag = (string?) input.Tag ?? "";
            string colorStr = (string?) input.Color ?? "";
            Color color = !colorStr.IsNullOrEmpty() ? ColorHelpers.HexToColor(colorStr) : chat.Settings.ColorBroadcast;

            return chat.Broadcast(text, tag, color).ToDetailedFrontendChat();
        }
    }
}
