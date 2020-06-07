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
    public class DataHandshakeTCPUDPClient : DataHandshakeClient<DataHandshakeTCPUDPClient> {

        static DataHandshakeTCPUDPClient() {
            DataID = "hsTUC";
        }

        public int UDPPort;

        public override void Read(DataContext ctx, BinaryReader reader) {
            base.Read(ctx, reader);

            UDPPort = reader.ReadInt32();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            base.Write(ctx, writer);

            writer.Write(UDPPort);
        }

    }
}
