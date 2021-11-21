using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataUnparsed : DataType<DataUnparsed> {

        public override DataFlags DataFlags => InnerFlags & ~DataFlags.Small;

        public string InnerID = "";
        public string InnerSource = "";
        public DataFlags InnerFlags;
        public byte[] InnerData = Dummy<byte>.EmptyArray;

        public override string GetTypeID(DataContext ctx)
            => InnerID;

        public override string GetSource(DataContext ctx)
            => InnerSource;

        protected override void Read(CelesteNetBinaryReader reader) {
            throw new InvalidOperationException("Can't read unparsed DataTypes");
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(InnerData);
        }

    }
}
