namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelUDPInfo : DataType<DataLowLevelUDPInfo> {

        static DataLowLevelUDPInfo() {
            DataID = "udpInfo";
        }

        public override DataFlags DataFlags => DataFlags.SlimHeader;

        public int MaxDatagramSize;
        public bool DisableUDP;

        protected override void Read(CelesteNetBinaryReader reader) {
            MaxDatagramSize = reader.Read7BitEncodedInt();
            DisableUDP = reader.ReadBoolean();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedInt(MaxDatagramSize);
            writer.Write(DisableUDP);
        }

    }
}