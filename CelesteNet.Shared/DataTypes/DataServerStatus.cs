namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataServerStatus : DataType<DataServerStatus> {

        static DataServerStatus() {
            DataID = "serverStatus";
        }

        public string Text = "";
        public float Time;
        public bool Spin = true;

        protected override void Read(CelesteNetBinaryReader reader) {
            Text = reader.ReadNetString();
            Time = reader.ReadSingle();
            Spin = reader.ReadBoolean();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Text);
            writer.Write(Time);
            writer.Write(Spin);
        }

    }
}
