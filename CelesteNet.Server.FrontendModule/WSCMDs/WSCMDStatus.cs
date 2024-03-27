using Celeste.Mod.CelesteNet.DataTypes;
using Newtonsoft.Json.Linq;

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class WSCMDStatus : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(object? input) {
            if (input == null)
                return false;

            if (input is string inputText) {
                Frontend.Server.BroadcastAsync(new DataServerStatus {
                    Text = inputText
                });
                return true;
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

            return true;
        }
    }
}
