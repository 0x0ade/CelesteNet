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
    public abstract class DataHandshakeClient<T> : DataType<T> where T : DataHandshakeClient<T> {

        public override bool IsValid => true;
        public override bool IsSendable => true;

        public string Name;

        public override void Read(BinaryReader reader) {
            Name = reader.ReadNullTerminatedString();
        }

        public override void Write(BinaryWriter writer) {
            writer.WriteNullTerminatedString(Name);
        }

    }
}
