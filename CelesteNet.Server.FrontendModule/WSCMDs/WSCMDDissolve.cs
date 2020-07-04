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
    public class WSCMDDissolve : WSCMD<uint> {
        public override bool Auth => true;
        public override object? Run(uint input) {
            if (input == 0)
                return null;

            ChatModule chat = Frontend.Server.Get<ChatModule>();

            Channels channels = Frontend.Server.Channels;
            lock (channels.All)
                if (channels.ByID.TryGetValue(input, out Channel? c))
                    foreach (CelesteNetPlayerSession player in c.Players.ToArray()) {
                        channels.Move(player, channels.Default.Name);
                        chat.SendTo(player, $"{c.Name} dissolved by server admin.", color: chat.Settings.ColorCommandReply);
                    }
            return null;
        }
    }
}
