namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class DataChannelMove : DataType<DataChannelMove> {

        static DataChannelMove() {
            DataID = "channelMove";
        }

        public DataPlayerInfo? Player;

        protected override void Read(CelesteNetBinaryReader reader) {
            Player = reader.ReadRef<DataPlayerInfo>();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteRef(Player);
        }

    }
}
