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
    public class DataNetEmoji : DataType<DataNetEmoji> {

        static DataNetEmoji() {
            DataID = "netemoji";
        }

        public override DataFlags DataFlags => DataFlags.Big;

        public string ID = "";
        public byte[] Data = Dummy<byte>.EmptyArray;

        public override void Read(DataContext ctx, BinaryReader reader) {
            ID = reader.ReadNetString();
            Data = reader.ReadBytes(reader.ReadInt32());
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNetString(ID);
            writer.Write(Data.Length);
            writer.Write(Data);
        }

    }
}
