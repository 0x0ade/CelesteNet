using Celeste.Mod.CelesteNet.Server.Chat;

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class WSCMDChat : WSCMD<string> {
        public override bool MustAuth => true;
        public override object? Run(string input) {
            Frontend.Server.Get<ChatModule>().Broadcast(input);
            return null;
        }
    }
}
