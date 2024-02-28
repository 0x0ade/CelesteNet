using System;

namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class DataMapModInfo : DataType<DataMapModInfo>, IDataRequestable<DataMapModInfoRequest> {

        static DataMapModInfo() {
            DataID = "mapModInfo";
        }

        public override DataFlags DataFlags => DataFlags.Taskable;

        public uint RequestID = uint.MaxValue;

        public string MapSID = "";
        public string MapName = "";
        public string ModID = "";
        public string ModName = "";
        public Version ModVersion = new();

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequestResponse(RequestID)
            };

        public override void FixupMeta(DataContext ctx) {
            RequestID = Get<MetaRequestResponse>(ctx);
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            MapSID = reader.ReadNetString();
            MapName = reader.ReadNetString();
            ModID = reader.ReadNetString();
            ModName = reader.ReadNetString();
            ModVersion = new(reader.ReadNetString());
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(MapSID);
            writer.WriteNetString(MapName);
            writer.WriteNetString(ModID);
            writer.WriteNetString(ModName);
            writer.WriteNetString(ModVersion.ToString());
        }

    }

    public class DataMapModInfoRequest : DataType<DataMapModInfoRequest> {

        static DataMapModInfoRequest() {
            DataID = "mapModInfoReq";
        }

        public uint ID = uint.MaxValue;

        public string MapSID = "";

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequest(ID)
            };

        public override void FixupMeta(DataContext ctx) {
            ID = Get<MetaRequest>(ctx);
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            MapSID = reader.ReadNetString();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(MapSID);
        }

    }
}
