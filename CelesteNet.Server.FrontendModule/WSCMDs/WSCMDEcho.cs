namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDEcho : WSCMD {
        public override bool MustAuth => false;
        public override object? Run(object? data) {
            return data;
        }
    }
}
