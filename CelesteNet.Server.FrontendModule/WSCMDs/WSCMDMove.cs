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
    public class WSCMDMove : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            if (input == null)
                return false;

            uint id = (uint?) input.ID ?? uint.MaxValue;
            string to = (string?) input.To ?? (string?) input.Channel ?? Channels.NameDefault;

            if (!Frontend.Server.PlayersByID.TryGetValue(id, out CelesteNetPlayerSession? player))
                return false;

            Frontend.Server.Channels.Move(player, to);
            return true;
        }
    }
}
