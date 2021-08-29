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
    public class WSCMDAuth : WSCMD<string> {
        public override bool Auth => false;
        public override object? Run(string data) {
            if (data == Frontend.Settings.PasswordExec) {
                do {
                    data = Guid.NewGuid().ToString();
                } while (!Frontend.CurrentSessionKeys.Add(data) || !Frontend.CurrentSessionExecKeys.Add(data));
                WS.SessionKey = data;
                return data;
            }
            if (data == Frontend.Settings.Password) {
                do {
                    data = Guid.NewGuid().ToString();
                } while (!Frontend.CurrentSessionKeys.Add(data));
                Frontend.CurrentSessionKeys.Add(data);
                WS.SessionKey = data;
                return data;
            }
            return "";
        }
    }
}
