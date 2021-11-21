﻿using Microsoft.Xna.Framework;
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
        public Version ModVersion = new();

        protected override void Read(CelesteNetBinaryReader reader) {
            ModID = reader.ReadNetString();
            ModName = reader.ReadNetString();
            ModVersion = new(reader.ReadNetString());
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(ModID);
            writer.WriteNetString(ModName);
            writer.WriteNetString(ModVersion.ToString());
        }

    }
}
