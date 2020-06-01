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

        public static string DataID;
        public static string Source;

        public virtual DataFlags DataFlags => DataFlags.None;
        public virtual bool IsSendable => true;
        public abstract bool IsValid { get; }

        public abstract void Read(BinaryReader reader);
        public abstract void Write(BinaryWriter writer);

        public abstract object Clone();

    }

    public abstract class DataType<T> : DataType where T : DataType<T> {
        static DataType() {
            DataID = typeof(T).Name;
            Source = typeof(T).Assembly.GetName().Name;
        }

        public T ReadT(BinaryReader reader) {
            Read(reader);
            return (T) this;
        }

        public override object Clone()
            => CloneT();
        public abstract T CloneT();
    }

    public class DataReferenceAttribute : Attribute {
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
