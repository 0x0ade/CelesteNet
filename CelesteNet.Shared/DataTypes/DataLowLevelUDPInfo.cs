namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelUDPInfo : DataType<DataLowLevelUDPInfo> {

        static DataLowLevelUDPInfo() {
            DataID = "udpInfo";
        }

        public override DataFlags DataFlags => DataFlags.SlimHeader | DataFlags.Small;

        public int ConnectionID;
        public int MaxDatagramSize;

        protected override void Read(CelesteNetBinaryReader reader) {
            ConnectionID = reader.Read7BitEncodedInt();
            MaxDatagramSize = reader.Read7BitEncodedInt();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedInt(ConnectionID);
            writer.Write7BitEncodedInt(MaxDatagramSize);
        }

    }
}