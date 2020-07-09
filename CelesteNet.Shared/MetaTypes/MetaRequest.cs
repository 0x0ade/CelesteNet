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
    public class MetaRequest : MetaType<MetaRequest> {

        static MetaRequest() {
            MetaID = "req";
        }

        public uint ID;

        public MetaRequest() {
        }
        public MetaRequest(uint id) {
            ID = id;
        }

        public override void Read(DataContext ctx, MetaTypeWrap data) {
            ID = uint.Parse(data["ID"]);
        }

        public override void Write(DataContext ctx, MetaTypeWrap data) {
            data["ID"] = ID.ToString();
        }

        public static implicit operator uint(MetaRequest meta)
            => meta.ID;

    }
}
