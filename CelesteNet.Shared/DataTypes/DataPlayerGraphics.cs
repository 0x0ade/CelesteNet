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
    public class DataPlayerGraphics : DataType<DataPlayerGraphics> {

        static DataPlayerGraphics() {
            DataID = "playerGraphics";
        }

        public override DataFlags DataFlags => DataFlags.CoreType;

        public DataPlayerInfo? Player;

        public int Depth;
        public PlayerSpriteMode SpriteMode;
        public float SpriteRate;
        public string[] SpriteAnimations = Dummy<string>.EmptyArray;

        public byte HairCount;
        public Vector2 HairStepPerSegment;
        public float HairStepInFacingPerSegment;
        public float HairStepApproach;
        public float HairStepYSinePerSegment;
        public Vector2[] HairScales = Dummy<Vector2>.EmptyArray;
        public string[] HairTextures = Dummy<string>.EmptyArray;

        public override bool FilterHandle(DataContext ctx)
            => Player != null; // Can be RECEIVED BY CLIENT TOO EARLY because UDP is UDP.

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerPublicState(Player),
                new MetaBoundRef(DataPlayerInfo.DataID, Player?.ID ?? uint.MaxValue, true)
            };

        public override void FixupMeta(DataContext ctx) {
            Player = Get<MetaPlayerPublicState>(ctx);
            Get<MetaBoundRef>(ctx).ID = Player?.ID ?? uint.MaxValue;
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            Depth = reader.ReadInt32();
            SpriteMode = (PlayerSpriteMode) reader.Read7BitEncodedInt();
            SpriteRate = reader.ReadSingle();
            SpriteAnimations = new string[reader.Read7BitEncodedInt()];
            for (int i = 0; i < SpriteAnimations.Length; i++)
                SpriteAnimations[i] = reader.ReadNetMappedString();

            HairCount = reader.ReadByte();
            HairStepPerSegment = reader.ReadVector2();
            HairStepInFacingPerSegment = reader.ReadSingle();
            HairStepApproach = reader.ReadSingle();
            HairStepYSinePerSegment = reader.ReadSingle();
            HairScales = new Vector2[HairCount];
            for (int i = 0; i < HairCount; i++)
                HairScales[i] = reader.ReadVector2Scale();
            HairTextures = new string[HairCount];
            for (int i = 0; i < HairCount; i++) {
                HairTextures[i] = reader.ReadNetMappedString();
                if (string.IsNullOrEmpty(HairTextures[i]) && i > 0)
                    HairTextures[i] = HairTextures[i - 1];
            }
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(Depth);
            writer.Write7BitEncodedInt((int) SpriteMode);
            writer.Write(SpriteRate);
            writer.Write7BitEncodedInt(SpriteAnimations.Length);
            for (int i = 0; i < SpriteAnimations.Length; i++)
                writer.WriteNetMappedString(SpriteAnimations[i]);

            writer.Write(HairCount);
            writer.Write(HairStepPerSegment);
            writer.Write(HairStepInFacingPerSegment);
            writer.Write(HairStepApproach);
            writer.Write(HairStepYSinePerSegment);
            for (int i = 0; i < HairCount; i++)
                writer.Write(HairScales[i]);
            for (int i = 0; i < HairCount; i++) {
                if (i > 0 && HairTextures[i] == HairTextures[i - 1])
                    writer.WriteNetMappedString(null);
                else
                    writer.WriteNetMappedString(HairTextures[i]);
            }
        }

    }
}
