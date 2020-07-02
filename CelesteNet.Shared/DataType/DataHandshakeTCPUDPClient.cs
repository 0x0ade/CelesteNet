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

        public override DataFlags DataFlags => base.DataFlags | (IsUDP ? DataFlags.Update : 0);

        public bool IsUDP;
        public uint ConnectionToken;

        public override void Read(DataContext ctx, BinaryReader reader) {
            base.Read(ctx, reader);

            ConnectionToken = reader.ReadUInt32();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            base.Write(ctx, writer);

            writer.Write(ConnectionToken);
        }

    }
}
