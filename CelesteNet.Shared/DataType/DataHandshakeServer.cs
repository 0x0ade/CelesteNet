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
    public class DataHandshakeServer : DataType<DataHandshakeServer> {

        static DataHandshakeServer() {
            DataID = "hsS";
        }

        public ushort Version;

        public DataPlayerInfo PlayerInfo;

        public override void Read(DataContext ctx, BinaryReader reader) {
            Version = reader.ReadUInt16();

            PlayerInfo = new DataPlayerInfo().ReadT(ctx, reader);
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write(Version);

            PlayerInfo.Write(ctx, writer);
        }

        public override DataHandshakeServer CloneT()
            => new DataHandshakeServer {
                Version = Version,

                PlayerInfo = PlayerInfo.CloneT()
            };

        public override string ToString()
            => $"{Version}, {PlayerInfo}";

    }
}
