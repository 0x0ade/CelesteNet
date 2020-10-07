using Microsoft.Xna.Framework;
using Monocle;
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
    public class DataLowLevelStringMapping : DataType<DataLowLevelStringMapping> {

        static DataLowLevelStringMapping() {
            DataID = "llsm";
        }

        public override DataFlags DataFlags => IsUpdate ? DataFlags.Update : DataFlags.None;

        public bool IsUpdate;

        public string StringMap = "";
        public string Value = "";
        public ushort ID;

        public override bool FilterHandle(DataContext ctx)
            => false;

        public override void Read(CelesteNetBinaryReader reader) {
            StringMap = reader.ReadNetString();
            Value = reader.ReadNetString();
            ID = reader.ReadUInt16();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(StringMap);
            writer.WriteNetString(Value);
            writer.Write(ID);
        }

    }
}
