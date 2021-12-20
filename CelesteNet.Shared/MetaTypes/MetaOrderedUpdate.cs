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
    public class MetaOrderedUpdate : MetaType<MetaOrderedUpdate> {

        static MetaOrderedUpdate() {
            MetaID = "ordered";
        }

        public uint ID;
        public byte? UpdateID;

        public MetaOrderedUpdate() {
        }
        public MetaOrderedUpdate(uint id) {
            ID = id;
        }

        public override void Read(CelesteNetBinaryReader reader) {
            ID = reader.Read7BitEncodedUInt();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedUInt(ID);
        }

    }
}
