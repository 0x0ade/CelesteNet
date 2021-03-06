﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class MetaOrderedUpdate : MetaType<MetaOrderedUpdate> {

        static MetaOrderedUpdate() {
            MetaID = "ordered";
        }

        public uint ID;
        public uint UpdateID;

        public MetaOrderedUpdate() {
        }
        public MetaOrderedUpdate(uint id, uint updateID) {
            ID = id;
            UpdateID = updateID;
        }

        public override void Read(DataContext ctx, MetaTypeWrap data) {
            ID = uint.Parse(data["ID"]);
            UpdateID = uint.Parse(data["UpdateID"]);
        }

        public override void Write(DataContext ctx, MetaTypeWrap data) {
            data["ID"] = ID.ToString();
            data["UpdateID"] = UpdateID.ToString();
        }

    }
}
