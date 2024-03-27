namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class WSCMDMove : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            if (input == null)
                return false;

            uint id = (uint?) input.ID ?? uint.MaxValue;
            string to = (string?) input.To ?? (string?) input.Channel ?? Channels.NameDefault;

            if (!Frontend.Server.PlayersByID.TryGetValue(id, out CelesteNetPlayerSession? player))
                return false;

            Frontend.Server.Channels.Move(player, to);
            return true;
        }
    }
}
