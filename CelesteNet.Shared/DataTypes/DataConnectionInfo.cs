namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class DataConnectionInfo : DataType<DataConnectionInfo> {

        static DataConnectionInfo() {
            DataID = "conInfo";
        }

        public override DataFlags DataFlags => DataFlags.CoreType;

        public DataPlayerInfo? Player;
        public int TCPPingMs;
        public int? UDPPingMs;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerPublicState(Player),
                new MetaBoundRef(DataPlayerInfo.DataID, Player?.ID ?? uint.MaxValue, true)
            };

        public override void FixupMeta(DataContext ctx) {
            Player = Get<MetaPlayerPublicState>(ctx);
            Get<MetaBoundRef>(ctx).ID = Player?.ID ?? uint.MaxValue;
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            TCPPingMs = reader.Read7BitEncodedInt();

            bool hasUDP = reader.ReadBoolean();
            if (hasUDP)
                UDPPingMs = reader.Read7BitEncodedInt();
            else
                UDPPingMs = null;
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedInt(TCPPingMs);

            writer.Write(UDPPingMs.HasValue);
            if (UDPPingMs.HasValue)
                writer.Write7BitEncodedInt(UDPPingMs.Value);
        }

    }
}
