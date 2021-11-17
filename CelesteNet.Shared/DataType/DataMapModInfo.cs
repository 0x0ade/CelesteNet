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

        public override void Read(CelesteNetBinaryReader reader) {
            MapSID = reader.ReadNetString();
            MapName = reader.ReadNetString();
            ModID = reader.ReadNetString();
            ModName = reader.ReadNetString();
            ModVersion = new(reader.ReadNetString());
        }

        public override void Write(CelesteNetBinaryWriter writer) {
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

        public override void Read(CelesteNetBinaryReader reader) {
            MapSID = reader.ReadNetString();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(MapSID);
        }

    }
}
