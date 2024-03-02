namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class WSCMDAuth : WSCMD<string> {
        public override bool MustAuth => false;
        public override object? Run(string data) {
            if (data == Frontend.Settings.PasswordExec) {
                WS.SessionKey = Frontend.GetNewKey(execAuth: true);
                return WS.SessionKey;
            }
            if (data == Frontend.Settings.Password) {
                WS.SessionKey = Frontend.GetNewKey();
                return WS.SessionKey;
            }
            return "";
        }
    }
}
