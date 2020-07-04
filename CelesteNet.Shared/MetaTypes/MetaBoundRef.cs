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
    public class MetaBoundRef : MetaType<MetaBoundRef> {

        static MetaBoundRef() {
            MetaID = "bound";
        }

        public string TypeBoundTo = "";
        public uint ID;
        public bool IsAlive;

        public MetaBoundRef() {
        }
        public MetaBoundRef(string typeBoundTo, uint id, bool isAlive) {
            TypeBoundTo = typeBoundTo;
            ID = id;
            IsAlive = isAlive;
        }

        public override void Read(DataContext ctx, MetaTypeWrap data) {
            TypeBoundTo = data["Type"];
            ID = uint.Parse(data["ID"]);
            IsAlive = bool.Parse(data["IsAlive"]);
        }

        public override void Write(DataContext ctx, MetaTypeWrap data) {
            data["Type"] = TypeBoundTo;
            data["ID"] = ID.ToString();
            data["IsAlive"] = IsAlive.ToString();
        }

    }
}
