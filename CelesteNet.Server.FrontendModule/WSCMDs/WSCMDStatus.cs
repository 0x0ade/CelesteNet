using Celeste.Mod.CelesteNet.DataTypes;

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

            Frontend.Server.BroadcastAsync(status);

            return null;
        }
    }
}
