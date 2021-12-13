namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelStringMap : DataType<DataLowLevelStringMap> {

        static DataLowLevelStringMap() {
            DataID = "stringMap";
        }

        public override DataFlags DataFlags => DataFlags.CoreType;

        public string String = string.Empty;
        public int ID;

        protected override void Read(CelesteNetBinaryReader reader) {
            String = reader.ReadNetString();
            ID = reader.ReadInt32();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(String);
            writer.Write(ID);
        }

    }
}