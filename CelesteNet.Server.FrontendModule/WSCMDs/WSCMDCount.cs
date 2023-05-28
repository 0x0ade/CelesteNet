namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDCount : WSCMD {
        public override bool MustAuth => false;
        public int Counter;
        public override object? Run(object? input) {
            return ++Counter;
        }
    }
}
