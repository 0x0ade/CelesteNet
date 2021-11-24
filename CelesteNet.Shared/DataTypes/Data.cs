using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public abstract class DataType {

        public virtual DataFlags DataFlags => DataFlags.None;

        public virtual MetaType[] Meta { get; set; } = Dummy<MetaType>.EmptyArray;

        public virtual bool FilterHandle(DataContext ctx) => true;
        public virtual bool FilterSend(DataContext ctx) => true;

        public virtual MetaType[] GenerateMeta(DataContext ctx)
            => Meta;

        public virtual void FixupMeta(DataContext ctx) {
        }

        public virtual MetaUpdateContext UpdateMeta(DataContext ctx)
            => new(ctx, this);

        public virtual void ReadAll(CelesteNetBinaryReader reader) {
            Meta = ReadMeta(reader);
            FixupMeta(reader.Data);
            Read(reader);
        }

        public virtual void WriteAll(CelesteNetBinaryWriter writer) {
            Meta = GenerateMeta(writer.Data);
            WriteMeta(writer, Meta);
            Write(writer);
        }

        protected virtual MetaType[] ReadMeta(CelesteNetBinaryReader reader) {
            MetaType[] meta = new MetaType[reader.ReadByte()];
            for (int i = 0; i < meta.Length; i++)
                meta[i] = reader.Data.ReadMeta(reader);
            return meta;
        }

        protected virtual void WriteMeta(CelesteNetBinaryWriter writer, MetaType[] meta) {
            writer.Write((byte) meta.Length);
            foreach (MetaType m in meta)
                writer.Data.WriteMeta(writer, m);
        }

        protected virtual void Read(CelesteNetBinaryReader reader) {}
        protected virtual void Write(CelesteNetBinaryWriter writer) {}

        public virtual bool Is<T>(DataContext ctx) where T : MetaType<T> {
            foreach (MetaType meta in Meta)
                if (meta is T)
                    return true;
            return false;
        }

        public virtual T Get<T>(DataContext ctx) where T : MetaType<T>
            => TryGet(ctx, out T? value) ? value : throw new ArgumentException($"DataType {ctx.DataTypeToID[GetType()]} doesn't have MetaType {ctx.MetaTypeToID[typeof(T)]}.");

        public virtual T? GetOpt<T>(DataContext ctx) where T : MetaType<T>
            => TryGet(ctx, out T? value) ? value : null;

        public virtual bool TryGet<T>(DataContext ctx, [NotNullWhen(true)] out T? value) where T : MetaType<T> {
            foreach (MetaType meta in Meta)
                if (meta is T cast) {
                    value = cast;
                    return true;
                }
            value = null;
            return false;
        }

        public virtual void Set<T>(DataContext ctx, T? value) where T : MetaType<T> {
            MetaType[] metas = Meta;

            if (value == null) {
                for (int i = 0; i < metas.Length; i++) {
                    MetaType meta = metas[i];
                    if (meta is T) {
                        if (i != metas.Length - 1)
                            Array.Copy(metas, i + 1, metas, i, metas.Length - i - 1);
                        Array.Resize(ref metas, metas.Length - 1);
                        Meta = metas;
                        return;
                    }
                }
                return;
            }

            for (int i = 0; i < metas.Length; i++) {
                MetaType meta = metas[i];
                if (meta == value || meta is T) {
                    metas[i] = value;
                    Meta = metas;
                    return;
                }
            }

            Array.Resize(ref metas, metas.Length + 1);
            metas[metas.Length - 1] = value;
            Meta = metas;
        }

        public virtual string GetTypeID(DataContext ctx)
            => ctx.DataTypeToID.TryGetValue(GetType(), out string? value) ? value : "";

        public virtual string GetSource(DataContext ctx)
            => ctx.DataTypeToSource.TryGetValue(GetType(), out string? value) ? value : "";

        public static byte PackBool(byte value, int index, bool set) {
            int mask = 1 << index;
            return set ? (byte) (value | mask) : (byte) (value & ~mask);
        }

        public static bool UnpackBool(byte value, int index) {
            int mask = 1 << index;
            return (value & mask) == mask;
        }

        public static byte PackBools(bool a = false, bool b = false, bool c = false, bool d = false, bool e = false, bool f = false, bool g = false, bool h = false) {
            byte value = 0;
            value = PackBool(value, 0, a);
            value = PackBool(value, 1, b);
            value = PackBool(value, 2, c);
            value = PackBool(value, 3, d);
            value = PackBool(value, 4, e);
            value = PackBool(value, 5, f);
            value = PackBool(value, 6, g);
            value = PackBool(value, 7, h);
            return value;
        }

    }

    public abstract class DataType<T> : DataType where T : DataType<T> {

        public static string DataID;
        public static string DataSource;

        static DataType() {
            DataID = typeof(T).Name;
            DataSource = typeof(T).Assembly.GetName().Name ?? DataID;
        }

        public T ReadT(CelesteNetBinaryReader reader) {
            Read(reader);
            return (T) this;
        }

        public T ReadAllT(CelesteNetBinaryReader reader) {
            ReadAll(reader);
            return (T) this;
        }

        public override string GetTypeID(DataContext ctx)
            => DataID;

        public override string GetSource(DataContext ctx)
            => DataSource;

    }

    public class MetaUpdateContext : IDisposable {

        public readonly DataContext Context;
        public readonly DataType Data;

        public MetaUpdateContext(DataContext ctx, DataType data) {
            Context = ctx;
            Data = data;
            Data.Meta = data.GenerateMeta(Context);
        }

        public void Dispose() {
            Data.FixupMeta(Context);
        }

    }

    // Used for compile time verification and to make getting the request type easier to obtain.
    public interface IDataRequestable {
    }
    public interface IDataRequestable<T> : IDataRequestable where T : DataType<T>, new() {
    }

    [Flags]
    public enum DataFlags : ushort {
        None =
            0b0000000000000000,
        Unreliable =
            0b0000000000000001,
        Taskable =
            0b0001000000000000,
        Small =
            0b0010000000000000,
        SlimHeader =
            0b0000000000010000,
        NoStandardMeta =
            0b0000000000100000,
        InteralSlimIndicator =
            0b1000000000000000,

        RESERVED =
            0b1100110000001110
    }
}
