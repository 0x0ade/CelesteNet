using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
