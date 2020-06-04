using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDChatEdit : WSCMD {
        public override bool Auth => true;
        public override object Run(dynamic input) {
            ChatServer chat = Frontend.Server.Chat;
            DataChat msg;
            lock (chat.ChatLog)
                if (!Frontend.Server.Chat.ChatLog.TryGetValue((uint?) input.ID ?? uint.MaxValue, out msg))
                    return null;

            if (input.Color != null)
                msg.Color = Calc.HexToColor((string) input.Color);

            if (input.Text != null)
                msg.Text = (string) input.Text;

            chat.Resend(msg);
            return msg.ToFrontendChat();
        }
    }
}
