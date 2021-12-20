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

        public string ID = "";
        public bool MoreFragments;
        public byte[] Data = Dummy<byte>.EmptyArray;

        protected override void Read(CelesteNetBinaryReader reader) {
            ID = reader.ReadNetString();
            MoreFragments = reader.ReadBoolean();
            Data = reader.ReadBytes(reader.ReadInt32());
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(ID);
            writer.Write(MoreFragments);
            writer.Write(Data.Length);
            writer.Write(Data);
        }

    }
}
