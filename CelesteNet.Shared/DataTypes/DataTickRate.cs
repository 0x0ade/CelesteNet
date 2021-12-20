namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataTickRate : DataType<DataTickRate> {

        static DataTickRate() {
            DataID = "tickRate";
        }

        public float TickRate;

        public override DataFlags DataFlags => DataFlags.Small;

        protected override void Read(CelesteNetBinaryReader reader) {
            TickRate = reader.ReadSingle();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(TickRate);
        }

    }
}