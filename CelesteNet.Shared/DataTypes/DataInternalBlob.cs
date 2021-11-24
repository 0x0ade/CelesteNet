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
        public Part? PartFirst;
        public Part? PartLast;

        public DataInternalBlob(DataContext ctx, DataType data) {
            while (data is DataInternalBlob blob)
                data = blob.Data;
            Data = data;
            Data.Meta = Data.GenerateMeta(ctx);

            using (MemoryStream stream = new())
            using (CelesteNetBinaryBlobPartWriter writer = new(ctx, this, stream)) {
                data.WriteAll(writer);
                writer.SplitBytes();
            }
        }

        [return: NotNullIfNotNull("data")]
        public static DataInternalBlob? For(DataContext ctx, DataType? data)
            => data == null ? null : new DataInternalBlob(ctx, data);

        public Part PartNext() {
            Part part = new();

            if (PartLast == null) {
                PartFirst = part;
                PartLast = part;
                return part;
            }

            PartLast.Next = part;
            PartLast = part;
            return part;
        }

        public override DataFlags DataFlags => Data.DataFlags;

        public override MetaType[] Meta {
            get => Data.Meta;
            set => Data.Meta = value;
        }

        public override bool FilterHandle(DataContext ctx) => Data.FilterHandle(ctx);
        public override bool FilterSend(DataContext ctx) => Data.FilterSend(ctx);

        public override MetaType[] GenerateMeta(DataContext ctx) => Data.GenerateMeta(ctx);

        public override void FixupMeta(DataContext ctx) => Data.FixupMeta(ctx);

        public override MetaUpdateContext UpdateMeta(DataContext ctx) => Data.UpdateMeta(ctx);

        public override void ReadAll(CelesteNetBinaryReader reader) => throw new InvalidOperationException("Cann't read internal blobs");

        public override void WriteAll(CelesteNetBinaryWriter writer) {
            for (Part? part = PartFirst; part != null; part = part.Next)
                part.Dump(writer);
        }

        public override bool Is<T>(DataContext ctx) => Data.Is<T>(ctx);

        public override T Get<T>(DataContext ctx) => Data.Get<T>(ctx);

        // Stupid Roslyn "bug": [NotNullWhen(true)] doesn't work as the ? needed for it requires when T :, but this is override.
        public override bool TryGet<T>(DataContext ctx, [MaybeNullWhen(false)] out T value) => Data.TryGet(ctx, out value);

        // Stupid Roslyn "bug": ? can't be used as it requires when T :, but this is override.
        public override void Set<T>(DataContext ctx, [AllowNull] T value) => Data.Set(ctx, value);

        public override string GetTypeID(DataContext ctx) => Data.GetTypeID(ctx);

        public override string GetSource(DataContext ctx) => Data.GetSource(ctx);

        public class Part {

            public byte[]? Bytes;
            public int BytesIndex;
            public int BytesCount;

            public string? String;

            public int SizeDummy;

            public Part? Next;

            public void Dump(CelesteNetBinaryWriter writer) {
                if (Bytes != null)
                    writer.Write(Bytes, BytesIndex, BytesCount);

                if (String != null)
                    writer.WriteNetMappedString(String);

                if (SizeDummy == int.MaxValue)
                    writer.UpdateSizeDummy();
                else if (SizeDummy != 0)
                    writer.WriteSizeDummy(SizeDummy);
            }

        }

    }
}
