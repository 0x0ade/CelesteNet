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
        public override bool Auth => true;
        public override object? Run(dynamic? input) {
            if (input == null)
                return null;

            uint id = (uint?) input.ID ?? uint.MaxValue;
            string to = (string?) input.To ?? (string?) input.Channel ?? Channels.NameDefault;

            Channels channels = Frontend.Server.Channels;

            lock (channels.All) {
                foreach (Channel c in channels.All) {
                    CelesteNetPlayerSession p = c.Players.FirstOrDefault(p => p.ID == id);
                    if (p != null) {
                        channels.Move(p, to);
                        return null;
                    }
                }
            }

            return null;
        }
    }
}
