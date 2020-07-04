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
    public class MetaRef : MetaType<MetaRef> {

        static MetaRef() {
            MetaID = "ref";
        }

        public uint ID;
        public bool IsAlive;

        public MetaRef() {
        }
        public MetaRef(uint id, bool isAlive) {
            ID = id;
            IsAlive = isAlive;
        }

        public override void Read(DataContext ctx, MetaTypeWrap data) {
            ID = uint.Parse(data["ID"]);
            IsAlive = bool.Parse(data["IsAlive"]);
        }

        public override void Write(DataContext ctx, MetaTypeWrap data) {
            data["ID"] = ID.ToString();
            data["IsAlive"] = IsAlive.ToString();
        }

    }
}
