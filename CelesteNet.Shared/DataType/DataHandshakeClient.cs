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

        public override DataFlags DataFlags => DataFlags.Small;

        public ushort Version = CelesteNetUtils.Version;

        public string Name = "";

        public override void Read(DataContext ctx, BinaryReader reader) {
            Version = reader.ReadUInt16();

            Name = reader.ReadNullTerminatedString();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write(Version);

            writer.WriteNullTerminatedString(Name);
        }

    }
}
