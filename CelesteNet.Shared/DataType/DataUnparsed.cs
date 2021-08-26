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

        public override DataFlags DataFlags => (InnerFlags & ~DataFlags.Small & ~DataFlags.OnlyLatest & ~DataFlags.SkipDuplicate) | DataFlags.Big;

        public string InnerID = "";
        public string InnerSource = "";
        public DataFlags InnerFlags;
        public List<MetaTypeWrap> InnerMeta = Dummy<MetaTypeWrap>.EmptyList;
        public byte[] InnerData = Dummy<byte>.EmptyArray;

        private uint InnerMetaStatus;
        private uint InnerMetaArrayStatus;
        private MetaTypeWrap[]? InnerMetaArray;

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
                for (int i = 0; i < InnerMeta.Count; i++)
                    if (InnerMeta[i].ID == id) {
                        InnerMeta.RemoveAt(i);
                        InnerMetaStatus++;
                        return;
                    }
                return;
            }

            foreach (MetaTypeWrap meta in InnerMeta)
                if (meta.ID == id) {
                    meta.Wrap(ctx, value);
                    return;
                }

            MetaTypeWrap wrap = new();
            wrap.Wrap(ctx, value);
            InnerMeta.Add(wrap);
            InnerMetaStatus++;
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(InnerData);
        }

        public override MetaTypeWrap[] WrapMeta(DataContext ctx) {
            if (InnerMetaArray == null || InnerMetaStatus != InnerMetaArrayStatus) {
                InnerMetaArray = InnerMeta.ToArray();
                InnerMetaArrayStatus = InnerMetaStatus;
            }
            return InnerMetaArray;
        }

        public override void UnwrapMeta(DataContext ctx, MetaTypeWrap[] wraps)
            => InnerMeta = new(wraps);

        public override string GetTypeID(DataContext ctx)
            => InnerID;

        public override string GetSource(DataContext ctx)
            => InnerSource;

    }
}
