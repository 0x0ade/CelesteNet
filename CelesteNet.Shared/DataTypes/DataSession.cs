using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataSession : DataType<DataSession>, IDataRequestable<DataSessionRequest> {

        static DataSession() {
            DataID = "session";
        }

        public uint RequestID = uint.MaxValue;

        public bool InSession;

        public DataPartAudioState? Audio;
        public Vector2? RespawnPoint;
        public CelestePlayerInventory Inventory;
        public HashSet<string>? Flags;
        public HashSet<string>? LevelFlags;
        public HashSet<CelesteEntityID>? Strawberries;
        public HashSet<CelesteEntityID>? DoNotLoad;
        public HashSet<CelesteEntityID>? Keys;
        public List<CelesteSession.Counter>? Counters;
        public string? FurthestSeenLevel;
        public string? StartCheckpoint;
        public string? ColorGrade;
        public bool[]? SummitGems;
        public bool FirstLevel;
        public bool Cassette;
        public bool HeartGem;
        public bool Dreaming;
        public bool GrabbedGolden;
        public bool HitCheckpoint;
        public float LightingAlphaAdd;
        public float BloomBaseAdd;
        public float DarkRoomAlpha;
        public long Time;
        public CelesteSession.CoreModes CoreMode;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequestResponse(RequestID)
            };

        public override void FixupMeta(DataContext ctx) {
            RequestID = Get<MetaRequestResponse>(ctx);
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            InSession = reader.ReadBoolean();
            if (!InSession)
                return;

            byte bools;
            int count;

            if (reader.ReadBoolean()) {
                Audio = new();
                Audio.ReadAll(reader);
            }

            if (reader.ReadBoolean())
                RespawnPoint = new(reader.ReadSingle(), reader.ReadSingle());

            Inventory = new();
            bools = reader.ReadByte();
            Inventory.Backpack = UnpackBool(bools, 0);
            Inventory.DreamDash = UnpackBool(bools, 1);
            Inventory.NoRefills = UnpackBool(bools, 2);
            Inventory.Dashes = reader.ReadByte();

            Flags = new();
            count = reader.ReadByte();
            for (int i = 0; i < count; i++)
                Flags.Add(reader.ReadNetString());

            LevelFlags = new();
            count = reader.ReadByte();
            for (int i = 0; i < count; i++)
                LevelFlags.Add(reader.ReadNetString());

            Strawberries = new();
            count = reader.ReadByte();
            for (int i = 0; i < count; i++)
                Strawberries.Add(new(reader.ReadNetString(), reader.ReadInt32()));

            DoNotLoad = new();
            count = reader.ReadByte();
            for (int i = 0; i < count; i++)
                DoNotLoad.Add(new(reader.ReadNetString(), reader.ReadInt32()));

            Keys = new();
            count = reader.ReadByte();
            for (int i = 0; i < count; i++)
                Keys.Add(new(reader.ReadNetString(), reader.ReadInt32()));

            Counters = new();
            count = reader.ReadByte();
            for (int i = 0; i < count; i++)
                Counters.Add(new() {
                    Key = reader.ReadNetString(),
                    Value = reader.ReadInt32()
                });

            FurthestSeenLevel = reader.ReadNetString().Nullify();
            StartCheckpoint = reader.ReadNetString().Nullify();
            ColorGrade = reader.ReadNetString().Nullify();

            count = reader.ReadByte();
            SummitGems = new bool[count];
            for (int i = 0; i < count; i++) {
                if ((i % 8) == 0)
                    bools = reader.ReadByte();
                SummitGems[i] = UnpackBool(bools, i % 8);
            }

            bools = reader.ReadByte();
            FirstLevel = UnpackBool(bools, 0);
            Cassette = UnpackBool(bools, 1);
            HeartGem = UnpackBool(bools, 2);
            Dreaming = UnpackBool(bools, 3);
            GrabbedGolden = UnpackBool(bools, 4);
            HitCheckpoint = UnpackBool(bools, 5);

            LightingAlphaAdd = reader.ReadSingle();
            BloomBaseAdd = reader.ReadSingle();
            DarkRoomAlpha = reader.ReadSingle();

            Time = reader.ReadInt64();

            CoreMode = (CelesteSession.CoreModes) reader.ReadByte();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            if (!InSession) {
                writer.Write(false);
                return;
            }

            writer.Write(true);

#pragma warning disable CS8602 // These should be null if InSession is true. If they're null, a NRE is appropriate.

            byte bools;

            if (Audio != null) {
                writer.Write(true);
                Audio.WriteAll(writer);
            } else {
                writer.Write(false);
            }

            if (RespawnPoint != null) {
                writer.Write(true);
                writer.Write(RespawnPoint.Value.X);
                writer.Write(RespawnPoint.Value.Y);
            } else {
                writer.Write(false);
            }

            writer.Write(PackBools(Inventory.Backpack, Inventory.DreamDash, Inventory.NoRefills));
            writer.Write((byte) Inventory.Dashes);

            writer.Write((byte) Flags.Count);
            foreach (string value in Flags)
                writer.WriteNetString(value);

            writer.Write((byte) LevelFlags.Count);
            foreach (string value in LevelFlags)
                writer.WriteNetString(value);

            writer.Write((byte) Strawberries.Count);
            foreach (CelesteEntityID value in Strawberries) {
                writer.WriteNetString(value.Level);
                writer.Write(value.ID);
            }

            writer.Write((byte) DoNotLoad.Count);
            foreach (CelesteEntityID value in DoNotLoad) {
                writer.WriteNetString(value.Level);
                writer.Write(value.ID);
            }

            writer.Write((byte) Keys.Count);
            foreach (CelesteEntityID value in Keys) {
                writer.WriteNetString(value.Level);
                writer.Write(value.ID);
            }

            writer.Write((byte) Counters.Count);
            foreach (CelesteSession.Counter value in Counters) {
                writer.WriteNetString(value.Key);
                writer.Write(value.Value);
            }

            writer.WriteNetString(FurthestSeenLevel);
            writer.WriteNetString(StartCheckpoint);
            writer.WriteNetString(ColorGrade);

            writer.Write((byte) SummitGems.Length);
            bools = 0;
            for (int i = 0; i < SummitGems.Length; i++) {
                bools = PackBool(bools, i % 8, SummitGems[i]);
                if (((i + 1) % 8) == 0) {
                    writer.Write(bools);
                    bools = 0;
                }
            }
            if (SummitGems.Length % 8 != 0)
                writer.Write(bools);

            writer.Write(PackBools(FirstLevel, Cassette, HeartGem, Dreaming, GrabbedGolden, HitCheckpoint));

            writer.Write(LightingAlphaAdd);
            writer.Write(BloomBaseAdd);
            writer.Write(DarkRoomAlpha);

            writer.Write(Time);

            writer.Write((byte) CoreMode);
#pragma warning restore CS8602
        }

    }

    public class DataSessionRequest : DataType<DataSessionRequest> {

        static DataSessionRequest() {
            DataID = "sessionReq";
        }

        public uint ID = uint.MaxValue;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequest(ID)
            };

        public override void FixupMeta(DataContext ctx) {
            ID = Get<MetaRequest>(ctx);
        }

    }
}
