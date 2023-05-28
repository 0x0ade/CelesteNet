namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataDisconnectReason : DataType<DataDisconnectReason> {

        static DataDisconnectReason() {
            DataID = "dcReason";
        }

        public string Text = "";

        protected override void Read(CelesteNetBinaryReader reader) {
            Text = reader.ReadNetString();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Text);
        }

    }
}
