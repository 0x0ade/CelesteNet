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
    public class DataSessionRequest : DataType<DataSessionRequest> {

        static DataSessionRequest() {
            DataID = "sessionReq";
        }

        public override DataFlags DataFlags => DataFlags.Small;

        public uint ID = uint.MaxValue;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRequest(ID)
            };

        public override void FixupMeta(DataContext ctx) {
            ID = Get<MetaRequest>(ctx);
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
        }

    }
}
