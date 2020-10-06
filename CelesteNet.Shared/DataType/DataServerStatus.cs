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
    public class DataServerStatus : DataType<DataServerStatus> {

        static DataServerStatus() {
            DataID = "serverStatus";
        }

        public string Text = "";
        public float Time;
        public bool Spin = true;

        public override void Read(CelesteNetBinaryReader reader) {
            Text = reader.ReadNetString();
            Time = reader.ReadSingle();
            Spin = reader.ReadBoolean();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Text);
            writer.Write(Time);
            writer.Write(Spin);
        }

    }
}
