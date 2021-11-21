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

        public override void Read(CelesteNetBinaryReader reader) {
            ID = unchecked((uint) reader.Read7BitEncodedInt());
            IsAlive = reader.ReadBoolean();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedInt(unchecked((int) ID));
            writer.Write(IsAlive);
        }

        public static implicit operator uint(MetaRef meta)
            => meta.ID;

    }
}
