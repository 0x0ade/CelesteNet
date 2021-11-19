namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelStringMap : DataType<DataLowLevelStringMap> {

        static DataLowLevelStringMap() {
            DataID = "stringMap";
        }

        public override DataFlags DataFlags => DataFlags.SlimHeader;

        public string String = string.Empty;
        public int ID;

        public override void Read(CelesteNetBinaryReader reader) {
            String = reader.ReadNetString();
            ID = reader.ReadInt32();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(String);
            writer.Write(ID);
        }

    }
}