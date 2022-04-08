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
    public class DataLowLevelPingRequest : DataType<DataLowLevelPingRequest> {

        static DataLowLevelPingRequest() {
            DataID = "pingRequest";
        }

        public override DataFlags DataFlags => DataFlags.CoreType;

        public long PingTime;

        protected override void Read(CelesteNetBinaryReader reader) {
            PingTime = reader.ReadInt64();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(PingTime);
        }

    }
}
