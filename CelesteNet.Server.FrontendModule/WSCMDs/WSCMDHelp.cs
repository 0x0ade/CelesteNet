using System.Linq;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDHelp : WSCMD {
        public override bool MustAuth => false;
        public override object? Run(object? input) {
            return WS?.Commands.All.Keys.ToList();
        }
    }
}
