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
    public class WSCMDChat : WSCMD<string> {
        public override bool MustAuth => true;
        public override object? Run(string input) {
            Frontend.Server.Get<ChatModule>().Broadcast(input);
            return null;
        }
    }
}
