using System.Linq;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPicoState : DataType<DataPicoState> {
        public DataPlayerInfo? Player;
        public float Spr;
        public float X = float.NaN;
        public float Y = float.NaN;
        public int Type;
        public bool FlipY;
        public bool FlipX;
        public int Djump;

        public HairNode[] Hair = new HairNode[5] { new(), new(), new(), new(), new() };
        public int Level;

        public class HairNode {
            public float X = float.NaN;
            public float Y = float.NaN;
            public float Size = 0;

            public override string ToString()
            {
                return $"HairNode(x: {X}, y: {Y}, size: {Size})";
            }
        }

        static DataPicoState() {
            DataID = "picostate";
        }

        public override string ToString() {
            return $"DataPicoState(player: {Player}, x: {X}, y: {Y}, spr: {Spr}, type: {Type}, flipX: {FlipX}, flipY: {FlipY}, djump: {Djump}, level: {Level}, hair: [{string.Join(", ", Hair.Select(node => node.ToString()))}])";
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            Player = reader.ReadOptRef<DataPlayerInfo>();
            Spr = reader.ReadSingle();
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
            FlipX = reader.ReadBoolean();
            FlipY = reader.ReadBoolean();
            Djump = reader.ReadInt32();
            Type = reader.ReadInt32();
            Level = reader.ReadInt32();
            for (int i = 0; i < 5; i++) {
                HairNode node = Hair[i];
                node.X = reader.ReadSingle();
                node.Y = reader.ReadSingle();
                node.Size = reader.ReadSingle();
            }
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteOptRef(Player);
            writer.Write(Spr);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(FlipX);
            writer.Write(FlipY);
            writer.Write(Djump);
            writer.Write(Type);
            writer.Write(Level);
            for (int i = 0; i < 5; i++) {
                HairNode node = Hair[i];
                writer.Write(node.X);
                writer.Write(node.Y);
                writer.Write(node.Size);
            }
        }
    }
}