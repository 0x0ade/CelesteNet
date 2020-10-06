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
    public class DataDisconnectReason : DataType<DataDisconnectReason> {

        static DataDisconnectReason() {
            DataID = "dcReason";
        }

        public string Text = "";

        public override void Read(CelesteNetBinaryReader reader) {
            Text = reader.ReadNetString();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Text);
        }

    }
}
