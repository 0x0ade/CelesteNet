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

        public T ReadT(DataContext ctx, BinaryReader reader) {
            Read(ctx, reader);
            return (T) this;
        }

    }

    [AttributeUsage(AttributeTargets.Interface)]
    public class DataBehaviorAttribute : Attribute {

        public string? ID { get; protected set; }

        public DataBehaviorAttribute() {
        }

        public DataBehaviorAttribute(string id) {
            ID = id;
        }

    }

    public interface IDataOrderedUpdate {

        uint ID { get; }
        uint UpdateID { get; set; }

    }

    public interface IDataRef {

        uint ID { get; }
        bool IsAliveRef { get; }

    }

    public interface IDataBoundRef : IDataRef {
    }

    public interface IDataBoundRef<T> : IDataBoundRef where T : DataType<T>, IDataRef {
    }

    public interface IDataStatic {
    }

    public interface IDataRequestable {
    }

    public interface IDataRequestable<T> : IDataRequestable where T : DataType<T>, new() {
    }

    [Flags]
    public enum DataFlags : ushort {
        None =
            0b0000000000000000,
        Update =
            0b0000000000000001,
        ForceForward =
            0b0000000000000010,

        Reserved4 =
            0b0000100000000000,

        Modded =
            0b0001000000000000,

        Small =
            0b0010000000000000,
        Big =
            0b0100000000000000,

        Reserved1 =
            0b1000000000000000
    }
}
