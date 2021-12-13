﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class MetaRequestResponse : MetaType<MetaRequestResponse> {

        static MetaRequestResponse() {
            MetaID = "reqRes";
        }

        public uint ID;

        public MetaRequestResponse() {
        }
        public MetaRequestResponse(uint id) {
            ID = id;
        }

        public override void Read(CelesteNetBinaryReader reader) {
            ID = reader.Read7BitEncodedUInt();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedUInt(ID);
        }

        public static implicit operator uint(MetaRequestResponse meta)
            => meta.ID;

    }
}
