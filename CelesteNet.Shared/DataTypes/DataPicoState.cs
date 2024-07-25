namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPicoState : DataType<DataPicoState> {
        public uint ID;
        public float Spr;
        public float X;
        public float Y;
        public int Type;
        public bool FlipY;
        public bool FlipX;
        public int Djump;

        static DataPicoState() {
            DataID = "picostate";
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            ID = reader.ReadUInt32();
            Spr = reader.ReadSingle();
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
            FlipX = reader.ReadBoolean();
            FlipY = reader.ReadBoolean();
            Djump = reader.ReadInt32();
            Type = reader.ReadInt32();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(ID);
            writer.Write(Spr);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(FlipX);
            writer.Write(FlipY);
            writer.Write(Djump);
            writer.Write(Type);
        }
    }
}