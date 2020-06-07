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
    public abstract class DataType {

        public virtual DataFlags DataFlags => DataFlags.None;

        public virtual bool FilterHandle(DataContext ctx) => true;
        public virtual bool FilterSend(DataContext ctx) => true;

        public abstract void Read(DataContext ctx, BinaryReader reader);
        public abstract void Write(DataContext ctx, BinaryWriter writer);

    }

    public abstract class DataType<T> : DataType where T : DataType<T> {

        public static string DataID;
        public static string Source;

        static DataType() {
            DataID = typeof(T).Name;
            Source = typeof(T).Assembly.GetName().Name;
        }

        public T ReadT(DataContext ctx, BinaryReader reader) {
            Read(ctx, reader);
            return (T) this;
        }

    }

    // TODO: Interface this maybe?
    public abstract class DataUpdateType<T> : DataType<T> where T : DataUpdateType<T> {

        public override DataFlags DataFlags => DataFlags.Update;

        public uint UpdateID;

        public override void Read(DataContext ctx, BinaryReader reader) {
            UpdateID = reader.ReadUInt32();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write(UpdateID);
        }

    }

    public interface IDataRefType {

        uint ID { get; set; }
        bool IsAliveRef { get; }

    }

    public interface IDataBoundRefType : IDataRefType {
    }

    public interface IDataBoundRefType<T> : IDataBoundRefType where T : DataType<T>, IDataRefType {
    }

    [Flags]
    public enum DataFlags : ushort {
        None =
            0b0000000000000000,
        Update =
            0b0000000000000001,
        ForceForward =
            0b0000000000000010,

        Reserved =
            0b1000000000000000
    }
}
