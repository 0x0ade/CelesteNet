using System.Linq;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDMove : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            if (input == null)
                return null;

            uint id = (uint?) input.ID ?? uint.MaxValue;
            string to = (string?) input.To ?? (string?) input.Channel ?? Channels.NameDefault;

            Channels channels = Frontend.Server.Channels;

            lock (channels.All) {
                foreach (Channel c in channels.All) {
                    CelesteNetPlayerSession? p = c.Players.FirstOrDefault(p => p.SessionID == id);
                    if (p != null) {
                        channels.Move(p, to);
                        return null;
                    }
                }
            }

            return null;
        }
    }
}
