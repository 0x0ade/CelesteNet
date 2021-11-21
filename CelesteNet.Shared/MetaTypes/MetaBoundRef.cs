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

        public override void Read(CelesteNetBinaryReader reader) {
            TypeBoundTo = reader.ReadNetMappedString();
            ID = unchecked((uint) reader.Read7BitEncodedInt());
            IsAlive = reader.ReadBoolean();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetMappedString(TypeBoundTo);
            writer.Write7BitEncodedInt(unchecked((int) ID));
            writer.Write(IsAlive);
        }

    }
}
