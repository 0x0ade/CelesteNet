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
    public class DataInternalBlob : DataType {

        public DataType Data;
        public byte[] Bytes = Dummy<byte>.EmptyArray;

        public DataInternalBlob(DataContext ctx, DataType data) {
            Data = data;
            Bytes = ctx.ToBytes(data);
        }

        public static DataInternalBlob? For(DataContext ctx, DataType? data)
            => data == null ? null : new DataInternalBlob(ctx, data);

        public override void Read(DataContext ctx, BinaryReader reader) {
            throw new NotImplementedException();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            throw new NotImplementedException();
        }

    }
}
