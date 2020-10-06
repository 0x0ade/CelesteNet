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

        public override void Read(CelesteNetBinaryReader reader) {
            Version = reader.ReadUInt16();

            Name = reader.ReadNetString();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(Version);

            writer.WriteNetString(Name);
        }

    }
}
