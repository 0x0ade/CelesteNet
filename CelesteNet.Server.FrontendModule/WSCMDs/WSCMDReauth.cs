namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class WSCMDReauth : WSCMD<string> {
        public override bool MustAuth => false;
        public override object? Run(string data) {
            if (WS == null)
                return null;

            if (Frontend.CurrentSessionKeys.Contains(data)) {
                WS.SessionKey = data;
                return true;
            }
            return false;
        }
    }
}
