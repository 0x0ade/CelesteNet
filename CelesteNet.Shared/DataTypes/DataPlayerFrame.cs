using Microsoft.Xna.Framework;
using Monocle;
using System;

namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class DataPlayerFrame : DataType<DataPlayerFrame> {

        static DataPlayerFrame() {
            DataID = "playerFrame";
        }

        // Too many too quickly to make tasking worth it.
        public override DataFlags DataFlags => DataFlags.Unreliable | DataFlags.CoreType | DataFlags.NoStandardMeta;

        public DataPlayerInfo? Player;

        public Vector2 Position;
        public Vector2 Scale;
        public Color Color;
        public Facings Facing;
        public Vector2 Speed;

        public int CurrentAnimationID;
        public int CurrentAnimationFrame;

        public Color[] HairColors = Dummy<Color>.EmptyArray;
        public string HairTexture0 = string.Empty;
        public bool HairSimulateMotion;

        public Entity[] Followers = Dummy<Entity>.EmptyArray;

        public Entity? Holding;

        // for server internal use only
        public bool TransmitDashes = true;

        public int Dashes = 1;
        // TODO: Get rid of this, sync particles separately!
        public bool? DashWasB;
        public Vector2? DashDir;

        public bool Dead;

        public override bool FilterHandle(DataContext ctx)
            => Player != null; // Can be RECEIVED BY CLIENT TOO EARLY because UDP is UDP.

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerUpdate(Player),
                new MetaOrderedUpdate(Player?.ID ?? uint.MaxValue)
            };

        public override void FixupMeta(DataContext ctx) {
            MetaPlayerUpdate playerUpd = Get<MetaPlayerUpdate>(ctx);
            MetaOrderedUpdate order = Get<MetaOrderedUpdate>(ctx);

            order.ID = playerUpd;
            Player = playerUpd;
        }

        public override void ReadAll(CelesteNetBinaryReader reader) {
            Color UnpackColor(ushort packedCol) => new() {
                R = (byte) (((packedCol >> 0) & 0b11111) << 3),
                G = (byte) (((packedCol >> 5) & 0b11111) << 3),
                B = (byte) (((packedCol >> 10) & 0b11111) << 3),
                A = 255
            };

            Player = reader.ReadRef<DataPlayerInfo>();

            Flags flags = (Flags) reader.ReadByte();

            Position = new(reader.Read7BitEncodedInt(), reader.Read7BitEncodedInt());
            Scale = new(reader.ReadSByte() / 16f, reader.ReadSByte() / 16f);
            Color = UnpackColor(reader.ReadUInt16());
            Facing = ((flags & Flags.FacingLeft) != 0) ? Facings.Left : Facings.Right;
            Speed = new(reader.ReadInt16(), reader.ReadInt16());

            CurrentAnimationID = reader.Read7BitEncodedInt();
            CurrentAnimationFrame = reader.Read7BitEncodedInt();

            HairColors = new Color[reader.ReadByte()];
            Color lastHairCol = Color.White;
            for (int i = 0; i < HairColors.Length;) {
                // Read the first color byte
                byte firstColByte = reader.ReadByte();

                // If it has the highest bit set, it's a repeat count of the last color
                if ((firstColByte & (1<<7)) != 0) {
                    int repCount = firstColByte & ~(1<<7);
                    for (int j = 0; i < HairColors.Length && j < repCount; j++)
                        HairColors[i++] = lastHairCol;
                } else {
                    // Read the second color byte and unpack it
                    HairColors[i++] = lastHairCol = UnpackColor((ushort) ((firstColByte << 8) | reader.ReadByte()));
                }
            }

            HairTexture0 = reader.ReadNetMappedString();
            HairSimulateMotion = (flags & Flags.HairSimulateMotion) != 0;

            Followers = new Entity[reader.ReadByte()];
            for (int i = 0; i < Followers.Length; i++) {
                Entity f = new();
                f.Scale = new Vector2(reader.ReadSByte() / 16f, reader.ReadSByte() / 16f);
                f.Color = reader.ReadColor();
                f.Depth = reader.Read7BitEncodedInt();
                f.SpriteRate = reader.ReadSingle();
                f.SpriteJustify = reader.ReadBoolean() ? (Vector2?) reader.ReadVector2() : null;
                f.SpriteID = reader.ReadNetMappedString();
                if (f.SpriteID == NetStringCtrl.RepeatString) {
                    Entity p = Followers[i - 1];
                    f.SpriteID = p.SpriteID;
                    f.CurrentAnimationID = p.CurrentAnimationID;
                } else {
                    f.CurrentAnimationID = reader.ReadNetMappedString();
                }
                f.CurrentAnimationFrame = reader.Read7BitEncodedInt();
                Followers[i] = f;
            }

            if ((flags & Flags.Holding) != 0)
                Holding = new() {
                    Position = Position + new Vector2(reader.Read7BitEncodedInt(), reader.Read7BitEncodedInt()),
                    Scale = new Vector2(reader.ReadSByte() / 16f, reader.ReadSByte() / 16f),
                    Color = reader.ReadColor(),
                    Depth = reader.Read7BitEncodedInt(),
                    SpriteRate = reader.ReadSingle(),
                    SpriteJustify = reader.ReadBoolean() ? (Vector2?) reader.ReadVector2() : null,
                    SpriteID = reader.ReadNetMappedString(),
                    CurrentAnimationID = reader.ReadNetMappedString(),
                    CurrentAnimationFrame = reader.Read7BitEncodedInt()
                };

            if ((flags & Flags.Dashing) != 0) {
                DashWasB = ((flags & Flags.DashB) != 0);
                DashDir = Calc.AngleToVector((float) (reader.ReadByte() / 256f * 2*Math.PI), 1);
            } else {
                DashWasB = null;
                DashDir = null;
            }

            Dead = (flags & Flags.Dead) != 0;

            Meta = GenerateMeta(reader.Data);
        }

        public override void WriteAll(CelesteNetBinaryWriter writer) {
            ushort PackColor(Color col) => (ushort) (
                (col.R >> 3) << 0 |
                (col.G >> 3) << 5 |
                (col.B >> 3) << 10
            );

            FixupMeta(writer.Data);
            writer.WriteRef(Player);

            Flags flags = 0;
            if (Facing == Facings.Left)
                flags |= Flags.FacingLeft;
            if (HairSimulateMotion)
                flags |= Flags.HairSimulateMotion;
            if (DashDir != null && DashWasB != null)
                flags |= Flags.Dashing;
            if (DashWasB ?? false)
                flags |= Flags.DashB;
            if (Holding != null)
                flags |= Flags.Holding;
            if (Dead)
                flags |= Flags.Dead;
            writer.Write((byte) flags);

            writer.Write7BitEncodedInt((int) Position.X);
            writer.Write7BitEncodedInt((int) Position.Y);
            writer.Write((sbyte) Calc.Clamp((int) (Scale.X * 16), sbyte.MinValue, sbyte.MaxValue));
            writer.Write((sbyte) Calc.Clamp((int) (Scale.Y * 16), sbyte.MinValue, sbyte.MaxValue));
            writer.Write(PackColor(Color));
            writer.Write((short) Calc.Clamp((int) Speed.X, short.MinValue, short.MaxValue));
            writer.Write((short) Calc.Clamp((int) Speed.Y, short.MinValue, short.MaxValue));

            writer.Write7BitEncodedInt(CurrentAnimationID);
            writer.Write7BitEncodedInt(CurrentAnimationFrame);

            writer.Write((byte) HairColors.Length);
            for (int i = 0; i < HairColors.Length;) {
                int origIdx = i;
                ushort packedCol = PackColor(HairColors[i]);
                writer.Write((byte) ((packedCol >> 8) & 0xff));
                writer.Write((byte) ((packedCol >> 0) & 0xff));
                for (i++; i < HairColors.Length && HairColors[i] == HairColors[origIdx]; i++);
                if (origIdx+1 < i)
                    writer.Write((byte) ((1<<7) | (i-origIdx-1)));
            }

            writer.WriteNetMappedString(HairTexture0);

            writer.Write((byte) Followers.Length);
            if (Followers.Length != 0) {
                for (int i = 0; i < Followers.Length; i++) {
                    Entity f = Followers[i];
                    writer.Write((sbyte) Calc.Clamp((int) (f.Scale.X * 16), sbyte.MinValue, sbyte.MaxValue));
                    writer.Write((sbyte) Calc.Clamp((int) (f.Scale.Y * 16), sbyte.MinValue, sbyte.MaxValue));
                    writer.Write(f.Color);
                    writer.Write7BitEncodedInt(f.Depth);
                    writer.Write(f.SpriteRate);
                    if (f.SpriteJustify == null) {
                        writer.Write(false);
                    } else {
                        writer.Write(true);
                        writer.Write(f.SpriteJustify.Value);
                    }
                    if (i >= 1 &&
                        f.SpriteID == Followers[i - 1].SpriteID &&
                        f.CurrentAnimationID == Followers[i - 1].CurrentAnimationID) {
                        writer.WriteNetMappedString(NetStringCtrl.RepeatString);
                    } else {
                        writer.WriteNetMappedString(f.SpriteID);
                        writer.WriteNetMappedString(f.CurrentAnimationID);
                    }
                    writer.Write7BitEncodedInt(f.CurrentAnimationFrame);
                }
            }

            if (Holding != null) {
                Entity h = Holding;
                writer.Write7BitEncodedInt((int) (h.Position.X - Position.X));
                writer.Write7BitEncodedInt((int) (h.Position.Y - Position.Y));
                writer.Write((sbyte) Calc.Clamp((int) (h.Scale.X * 16), sbyte.MinValue, sbyte.MaxValue));
                writer.Write((sbyte) Calc.Clamp((int) (h.Scale.Y * 16), sbyte.MinValue, sbyte.MaxValue));
                writer.Write(h.Color);
                writer.Write7BitEncodedInt(h.Depth);
                writer.Write(h.SpriteRate);
                if (h.SpriteJustify == null) {
                    writer.Write(false);

                } else {
                    writer.Write(true);
                    writer.Write(h.SpriteJustify.Value);
                }
                writer.WriteNetMappedString(h.SpriteID);
                writer.WriteNetMappedString(h.CurrentAnimationID);
                writer.Write7BitEncodedInt(h.CurrentAnimationFrame);
            }

            if (DashDir != null && DashWasB != null)
                writer.Write((byte) ((DashDir.Value.Angle() / (2 * Math.PI) * 256f) % 256));
            //if (TransmitDashes)
                //writer.Write7BitEncodedInt(Dashes);
        }

        [Flags]
        private enum Flags {
            FacingLeft = 0b00000001,
            HairSimulateMotion = 0b00000010,
            Dashing = 0b00000100,
            DashB = 0b00001000,
            Holding = 0b00010000,
            Dead = 0b00100000,
        }

        public class Entity {
            public Vector2 Position;
            public Vector2 Scale;
            public Color Color;
            public int Depth;
            public float SpriteRate;
            public Vector2? SpriteJustify;
            public string SpriteID = "";
            public string CurrentAnimationID = "";
            public int CurrentAnimationFrame;
        }

    }
}
