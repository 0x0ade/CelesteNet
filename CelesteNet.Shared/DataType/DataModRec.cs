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
    public class DataModRec : DataType<DataModRec> {

        static DataModRec() {
            DataID = "modRec";
        }

        public string ModID = "";
        public string ModName = "";
        public Version ModVersion = new Version();

        public override void Read(DataContext ctx, BinaryReader reader) {
            ModID = reader.ReadNullTerminatedString();
            ModName = reader.ReadNullTerminatedString();
            ModVersion = new Version(reader.ReadNullTerminatedString());
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNullTerminatedString(ModID);
            writer.WriteNullTerminatedString(ModName);
            writer.WriteNullTerminatedString(ModVersion.ToString());
        }

    }
}
