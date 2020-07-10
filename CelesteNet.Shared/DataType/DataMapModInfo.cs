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

        public uint RequestID = uint.MaxValue;

        public string MapSID = "";
        public string MapName = "";
        public string ModID = "";
        public string ModName = "";
        public Version ModVersion = new Version();

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequestResponse(RequestID)
            };

        public override void FixupMeta(DataContext ctx) {
            RequestID = Get<MetaRequestResponse>(ctx);
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
            MapSID = reader.ReadNullTerminatedString();
            MapName = reader.ReadNullTerminatedString();
            ModID = reader.ReadNullTerminatedString();
            ModName = reader.ReadNullTerminatedString();
            ModVersion = new Version(reader.ReadNullTerminatedString());
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNullTerminatedString(MapSID);
            writer.WriteNullTerminatedString(MapName);
            writer.WriteNullTerminatedString(ModID);
            writer.WriteNullTerminatedString(ModName);
            writer.WriteNullTerminatedString(ModVersion.ToString());
        }

    }

    public class DataMapModInfoRequest : DataType<DataMapModInfoRequest> {

        static DataMapModInfoRequest() {
            DataID = "mapModInfoReq";
        }

        public override DataFlags DataFlags => DataFlags.Small;

        public uint ID = uint.MaxValue;

        public string MapSID = "";

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequest(ID)
            };

        public override void FixupMeta(DataContext ctx) {
            ID = Get<MetaRequest>(ctx);
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
            MapSID = reader.ReadNullTerminatedString();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNullTerminatedString(MapSID);
        }

    }
}
