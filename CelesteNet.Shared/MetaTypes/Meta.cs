using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public abstract class MetaType {

        public virtual string GetTypeID(DataContext ctx)
            => ctx.MetaTypeToID.TryGetValue(GetType(), out string? value) ? value : "";

        public abstract void Read(CelesteNetBinaryReader reader);
        public abstract void Write(CelesteNetBinaryWriter writer);

    }

    public abstract class MetaType<T> : MetaType where T : MetaType<T> {

        public static string MetaID;

        static MetaType() {
            MetaID = typeof(T).Name;
        }

        public T ReadT(CelesteNetBinaryReader reader) {
            Read(reader);
            return (T) this;
        }

    }
}
