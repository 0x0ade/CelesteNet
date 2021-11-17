namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelStringMap : DataType<DataLowLevelStringMap> {

        static DataLowLevelStringMap() {
            DataID = "stringMap";
        }

        public string String = string.Empty;
        public int ID;

        public override void Read(CelesteNetBinaryReader reader) {
            String = reader.ReadNetString();
            ID = reader.ReadInt32();
        }

        override public void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(String);
            writer.Write(ID);
        }

    }
}