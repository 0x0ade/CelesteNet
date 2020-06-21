using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

        public DataPartImage(Image img) {
            AtlasPath = img.Texture.AtlasPath;
            Position = img.Position;
            Origin = img.Origin;
            Scale = img.Scale;
            Rotation = img.Rotation;
            Color = img.Color;
            Effects = img.Effects;
        }

        public Image ToImage()
            => new Image(GFX.Game[AtlasPath]) {
                Position = Position,
                Origin = Origin,
                Scale = Scale,
                Rotation = Rotation,
                Color = Color,
                Effects = Effects
            };

        public override void Read(DataContext ctx, BinaryReader reader) {
            AtlasPath = reader.ReadNullTerminatedString();
            Position = reader.ReadVector2();
            Origin = reader.ReadVector2();
            Scale = reader.ReadVector2();
            Rotation = reader.ReadSingle();
            Color = reader.ReadColor();
            Effects = (SpriteEffects) reader.ReadByte();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNullTerminatedString(AtlasPath);
            writer.Write(Position);
            writer.Write(Origin);
            writer.Write(Scale);
            writer.Write(Rotation);
            writer.Write(Color);
            writer.Write((byte) Effects);
        }

    }
}
