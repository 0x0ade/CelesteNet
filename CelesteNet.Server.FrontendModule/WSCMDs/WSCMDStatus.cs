using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Newtonsoft.Json.Linq;
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
            if (data.Text.Type is JTokenType.String)
                status.Text = (string) data.Text;
            if (data.Time.Type is (JTokenType.Float or JTokenType.Integer))
                status.Time = (float) data.Time;
            if (data.Spin.Type is JTokenType.Boolean)
                status.Spin = (bool) data.Spin;

            Frontend.Server.BroadcastAsync(status);

            return null;
        }
    }
}
