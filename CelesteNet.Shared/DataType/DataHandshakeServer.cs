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

        public override bool IsValid => true;
        public override bool IsSendable => true;

        public ushort Version;

        public DataPlayer PlayerInfo;

        public override void Read(BinaryReader reader) {
            Version = reader.ReadUInt16();

            PlayerInfo = new DataPlayer().ReadT(reader);
        }

        public override void Write(BinaryWriter writer) {
            writer.Write(Version);

            PlayerInfo.Write(writer);
        }

        public override DataHandshakeServer CloneT()
            => new DataHandshakeServer {
                Version = Version,

                PlayerInfo = PlayerInfo.CloneT()
            };

    }
}
