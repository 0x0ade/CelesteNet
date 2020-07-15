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

        public override DataFlags DataFlags => (InnerFlags & ~DataFlags.Small) | DataFlags.Big;

        public string InnerID = "";
        public string InnerSource = "";
        public DataFlags InnerFlags;
        public MetaTypeWrap[] InnerMeta = Dummy<MetaTypeWrap>.EmptyArray;
        public byte[] InnerData = Dummy<byte>.EmptyArray;

        public override bool Is<T>(DataContext ctx) {
            string id = ctx.MetaTypeToID[typeof(T)];
            foreach (MetaTypeWrap meta in InnerMeta)
                if (meta.ID == id)
                    return true;
            return false;
        }

        // Stupid Roslyn "bug": [NotNullWhen(true)] doesn't work as the ? needed for it requires when T :, but this is override.
        public override bool TryGet<T>(DataContext ctx, [MaybeNullWhen(false)] out T value) {
            string id = ctx.MetaTypeToID[typeof(T)];
            foreach (MetaTypeWrap metaWrap in InnerMeta)
                if (metaWrap.ID == id) {
                    value = (T) metaWrap.Unwrap(ctx);
                    return true;
                }
            value = null;
            return false;
        }

        // Stupid Roslyn "bug": ? can't be used as it requires when T :, but this is override.
        public override void Set<T>(DataContext ctx, [AllowNull] T value) {
            string id = ctx.MetaTypeToID[typeof(T)];
            if (value == null) {
                InnerMeta = InnerMeta.Where(m => m.ID != id).ToArray();
                return;
            }

            for (int i = 0; i < InnerMeta.Length; i++) {
                MetaTypeWrap meta = InnerMeta[i];
                if (meta.ID == id) {
                    meta.Wrap(ctx, value);
                    return;
                }
            }

            MetaTypeWrap wrap = new MetaTypeWrap();
            wrap.Wrap(ctx, value);
            InnerMeta = InnerMeta.Concat(new MetaTypeWrap[] { wrap }).ToArray();
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
            throw new NotSupportedException();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write(InnerData);
        }

        public override MetaTypeWrap[] WrapMeta(DataContext ctx)
            => InnerMeta;

        public override void UnwrapMeta(DataContext ctx, MetaTypeWrap[] wraps)
            => InnerMeta = wraps;

        public override string GetTypeID(DataContext ctx)
            => InnerID;

        public override string GetSource(DataContext ctx)
            => InnerSource;

    }
}
