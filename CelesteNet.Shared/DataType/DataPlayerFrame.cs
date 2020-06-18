using Microsoft.Xna.Framework;
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
    public class DataPlayerFrame : DataType<DataPlayerFrame>, IDataOrderedUpdate, IDataPlayerUpdate {

        static DataPlayerFrame() {
            DataID = "playerFrame";
        }

        public override DataFlags DataFlags => DataFlags.Update;

        public uint ID => Player?.ID ?? uint.MaxValue;
        public uint UpdateID { get; set; }

        public DataPlayerInfo? Player { get; set; }

        public Vector2 Position;
        public Vector2 Speed;
        public Vector2 Scale;
        public Color Color;
        public Facings Facing;

        public PlayerSpriteMode SpriteMode;
        public float SpriteRate;
        public Vector2? SpriteJustify;

        public string CurrentAnimationID = "";
        public int CurrentAnimationFrame;

        public Color HairColor;
        public bool HairSimulateMotion;

        public byte HairCount;
        public Color[] HairColors = Dummy<Color>.EmptyArray;
        public string[] HairTextures = Dummy<string>.EmptyArray;

        public Color? DashColor;
        public Vector2 DashDir;
        public bool DashWasB;

        public bool Dead;

        public override bool FilterHandle(DataContext ctx)
            => Player != null; // Can be RECEIVED BY CLIENT TOO EARLY because UDP is UDP.

        public override void Read(DataContext ctx, BinaryReader reader) {
            UpdateID = reader.ReadUInt32();

            Player = ctx.ReadOptRef<DataPlayerInfo>(reader);

            Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            Speed = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            Scale = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            Color = new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            Facing = reader.ReadBoolean() ? Facings.Left : Facings.Right;

            SpriteMode = (PlayerSpriteMode) reader.ReadByte();
            SpriteRate = reader.ReadSingle();
            SpriteJustify = reader.ReadBoolean() ? (Vector2?) new Vector2(reader.ReadSingle(), reader.ReadSingle()) : null;

            CurrentAnimationID = reader.ReadNullTerminatedString();
            CurrentAnimationFrame = reader.ReadInt32();

            HairColor = new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            HairSimulateMotion = reader.ReadBoolean();

            HairCount = reader.ReadByte();
            HairColors = new Color[HairCount];
            for (int i = 0; i < HairColors.Length; i++) {
                HairColors[i] = new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            }
            HairTextures = new string[HairCount];
            for (int i = 0; i < HairColors.Length; i++) {
                HairTextures[i] = reader.ReadNullTerminatedString();
                if (HairTextures[i] == "-")
                    HairTextures[i] = HairTextures[i - 1];
            }

            DashColor = reader.ReadBoolean() ? (Color?) new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()) : null;
            DashDir = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            DashWasB = reader.ReadBoolean();

            Dead = reader.ReadBoolean();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write(UpdateID);

            ctx.WriteRef(writer, Player);

            writer.Write(Position.X);
            writer.Write(Position.Y);
            writer.Write(Speed.X);
            writer.Write(Speed.Y);
            writer.Write(Scale.X);
            writer.Write(Scale.Y);
            writer.Write(Color.R);
            writer.Write(Color.G);
            writer.Write(Color.B);
            writer.Write(Color.A);
            writer.Write(Facing == Facings.Left);

            writer.Write((byte) SpriteMode);
            writer.Write(SpriteRate);
            if (SpriteJustify != null) {
                writer.Write(true);
                writer.Write(SpriteJustify.Value.X);
                writer.Write(SpriteJustify.Value.Y);
            } else {
                writer.Write(false);
            }

            writer.WriteNullTerminatedString(CurrentAnimationID);
            writer.Write(CurrentAnimationFrame);

            writer.Write(HairColor.R);
            writer.Write(HairColor.G);
            writer.Write(HairColor.B);
            writer.Write(HairColor.A);
            writer.Write(HairSimulateMotion);

            writer.Write(HairCount);
            if (HairColors != null && HairCount!= 0) {
                for (int i = 0; i < HairCount; i++) {
                    writer.Write(HairColors[i].R);
                    writer.Write(HairColors[i].G);
                    writer.Write(HairColors[i].B);
                    writer.Write(HairColors[i].A);
                }
            }
            if (HairTextures != null && HairCount != 0) {
                for (int i = 0; i < HairCount; i++) {
                    if (i > 1 && HairTextures[i] == HairTextures[i - 1])
                        writer.WriteNullTerminatedString("-");
                    else
                        writer.WriteNullTerminatedString(HairTextures[i]);
                }
            }

            if (DashColor == null) {
                writer.Write(false);
            } else {
                writer.Write(true);
                writer.Write(DashColor.Value.R);
                writer.Write(DashColor.Value.G);
                writer.Write(DashColor.Value.B);
                writer.Write(DashColor.Value.A);
            }

            writer.Write(DashDir.X);
            writer.Write(DashDir.Y);
            writer.Write(DashWasB);

            writer.Write(Dead);
        }

    }
}
