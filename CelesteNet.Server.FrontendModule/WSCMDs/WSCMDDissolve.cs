using Celeste.Mod.CelesteNet.Server.Chat;
using System.Linq;

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class WSCMDDissolve : WSCMD<uint> {
        public override bool MustAuth => true;
        public override object? Run(uint input) {
            if (input == Channels.IdDefault)
                return false;

            ChatModule chat = Frontend.Server.Get<ChatModule>();

            Channels channels = Frontend.Server.Channels;
            lock (channels.All) {
                if (!channels.ByID.TryGetValue(input, out Channel? c))
                    return false;

                foreach (CelesteNetPlayerSession player in c.Players.ToArray()) {
                    channels.Move(player, Channels.NameDefault);
                    chat.SendTo(player, $"{c.Name} dissolved by server admin.", color: chat.Settings.ColorCommandReply);
                }
                return true;
            }
        }
    }
}
