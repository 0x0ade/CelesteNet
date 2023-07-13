using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPartImage : DataType<DataPartImage> {

        public string AtlasPath = "";
        public Vector2 Position;
        public Vector2 Origin;
        public Vector2 Scale;
        public float Rotation;
        public Color Color;
        public SpriteEffects Effects;

        public DataPartImage() {
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            AtlasPath = reader.ReadNetString();
            Position = reader.ReadVector2();
            Origin = reader.ReadVector2();
            Scale = reader.ReadVector2Scale();
            Rotation = reader.ReadSingle();
            Color = reader.ReadColor();
            Effects = (SpriteEffects) reader.ReadByte();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(AtlasPath);
            writer.Write(Position);
            writer.Write(Origin);
            writer.Write(Scale);
            writer.Write(Rotation);
            writer.Write(Color);
            writer.Write((byte) Effects);
        }

    }
}
