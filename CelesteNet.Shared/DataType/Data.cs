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
    
    public interface IDataOrderedUpdate {
    
        uint ID { get; }
        uint UpdateID { get; set; }

    }

    public interface IDataRef {

        uint ID { get; set; }
        bool IsAliveRef { get; }

    }

    public interface IDataBoundRef : IDataRef {
    }

    public interface IDataBoundRef<T> : IDataBoundRef where T : DataType<T>, IDataRef {
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
