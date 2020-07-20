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
    public class DataInternalBlob : DataType {

        public readonly DataType Data;
        public readonly byte[] Bytes = Dummy<byte>.EmptyArray;

        public DataInternalBlob(DataContext ctx, DataType data) {
            while (data is DataInternalBlob blob)
                data = blob.Data;
            Data = data;
            Bytes = ctx.ToBytes(data);
        }

        public static DataInternalBlob? For(DataContext ctx, DataType? data)
            => data == null ? null : new DataInternalBlob(ctx, data);

        public override DataFlags DataFlags => Data.DataFlags;

        public override MetaType[] Meta => Data.Meta;

        public override bool FilterHandle(DataContext ctx) => Data.FilterHandle(ctx);
        public override bool FilterSend(DataContext ctx) => Data.FilterSend(ctx);

        public override MetaType[] GenerateMeta(DataContext ctx) => Data.GenerateMeta(ctx);

        public override void FixupMeta(DataContext ctx) => Data.FixupMeta(ctx);

        public override MetaUpdateContext UpdateMeta(DataContext ctx) => Data.UpdateMeta(ctx);

        public override void ReadAll(DataContext ctx, BinaryReader reader) => Data.ReadAll(ctx, reader);

        public override void WriteAll(DataContext ctx, BinaryWriter writer) => Data.WriteAll(ctx, writer);

        public override void Read(DataContext ctx, BinaryReader reader) => Data.Read(ctx, reader);

        public override void Write(DataContext ctx, BinaryWriter writer) => Data.Write(ctx, writer);

        public override bool Is<T>(DataContext ctx) => Data.Is<T>(ctx);

        public override T Get<T>(DataContext ctx) => Data.Get<T>(ctx);

        // Stupid Roslyn "bug": [NotNullWhen(true)] doesn't work as the ? needed for it requires when T :, but this is override.
        public override bool TryGet<T>(DataContext ctx, [MaybeNullWhen(false)] out T value) => Data.TryGet(ctx, out value);

        // Stupid Roslyn "bug": ? can't be used as it requires when T :, but this is override.
        public override void Set<T>(DataContext ctx, [AllowNull] T value) => Data.Set(ctx, value);

        public override MetaTypeWrap[] WrapMeta(DataContext ctx) => Data.WrapMeta(ctx);

        public override void UnwrapMeta(DataContext ctx, MetaTypeWrap[] wraps) => Data.UnwrapMeta(ctx, wraps);

        public override string GetTypeID(DataContext ctx) => Data.GetTypeID(ctx);

        public override string GetSource(DataContext ctx) => Data.GetSource(ctx);

    }
}
