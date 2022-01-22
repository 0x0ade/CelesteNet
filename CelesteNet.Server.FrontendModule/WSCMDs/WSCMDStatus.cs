using Celeste.Mod.CelesteNet.DataTypes;
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
    public class WSCMDStatus : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(object? input) {
            if (input == null)
                return null;

            if (input is string inputText) {
                Frontend.Server.BroadcastAsync(new DataServerStatus {
                    Text = inputText
                });
                return null;
            }

            dynamic data = input;
            DataServerStatus status = new();
            if (data.Text is string text)
                status.Text = text;
            if (data.Time is float time)
                status.Time = time;
            if (data.Spin is bool spin)
                status.Spin = spin;

            return null;
        }
    }
}
