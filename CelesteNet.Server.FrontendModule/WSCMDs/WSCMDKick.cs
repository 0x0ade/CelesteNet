using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
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
    public class WSCMDKick : WSCMD<uint> {
        public override bool Auth => true;
        public override object? Run(uint input) {
            using (Frontend.Server.ConLock.R())
                if (Frontend.Server.PlayersByID.TryGetValue(input, out CelesteNetPlayerSession? player)) {
                    ChatModule chat = Frontend.Server.Get<ChatModule>();
                    new DynamicData(player).Set("leaveReason", chat.Settings.MessageKick);
                    player.Dispose();
                    player.Con.Send(new DataDisconnectReason { Text = "Kicked" });
                    player.Con.Send(new DataInternalDisconnect());
                    return true;
                }
            return null;
        }
    }
}
