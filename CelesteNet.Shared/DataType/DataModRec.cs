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
            ModID = reader.ReadNetString();
            ModName = reader.ReadNetString();
            ModVersion = new Version(reader.ReadNetString());
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNetString(ModID);
            writer.WriteNetString(ModName);
            writer.WriteNetString(ModVersion.ToString());
        }

    }
}
