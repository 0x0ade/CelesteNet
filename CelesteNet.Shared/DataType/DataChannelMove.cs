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

        public override DataFlags DataFlags => DataFlags.Small;

        public DataPlayerInfo? Player;

        public override void Read(DataContext ctx, BinaryReader reader) {
            Player = ctx.ReadRef<DataPlayerInfo>(reader);
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            ctx.WriteRef(writer, Player);
        }

    }
}
