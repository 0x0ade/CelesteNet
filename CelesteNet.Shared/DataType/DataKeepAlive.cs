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
    public class DataKeepAlive : DataType<DataKeepAlive> {

        static DataKeepAlive() {
            DataID = "keepalive";
        }

        public override DataFlags DataFlags => DataFlags.Small | (IsUpdate ? DataFlags.Update : DataFlags.None);

        public bool IsUpdate;

        public override bool FilterHandle(DataContext ctx)
            => false;

        public override void Read(CelesteNetBinaryReader reader) {
        }

        public override void Write(CelesteNetBinaryWriter writer) {
        }

    }
}
