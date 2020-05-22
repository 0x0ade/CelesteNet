using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Shared.DataTypes {
    public abstract class DataType {

        public static string ChunkID;
        public static string Source;

        public virtual bool IsSendable => true;
        public abstract bool IsValid { get; }

        public abstract void Read(BinaryReader reader);
        public abstract void Write(BinaryWriter writer);

        public abstract object Clone();

    }

    public abstract class DataType<T> : DataType where T : DataType<T> {
        static DataType() {
            ChunkID = typeof(T).Name;
            Source = typeof(T).Assembly.GetName().Name;
        }
    }
}
