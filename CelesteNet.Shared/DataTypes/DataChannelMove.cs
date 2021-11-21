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
    public class DataChannelMove : DataType<DataChannelMove> {

        static DataChannelMove() {
            DataID = "channelMove";
        }

        public DataPlayerInfo? Player;

        protected override void Read(CelesteNetBinaryReader reader) {
            Player = reader.ReadRef<DataPlayerInfo>();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteRef(Player);
        }

    }
}
