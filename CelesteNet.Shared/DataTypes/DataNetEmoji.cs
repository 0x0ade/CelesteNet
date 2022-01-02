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

        [Flags]
        public enum Flags {
            FIRST = 1, MORE = 2
        }

        static DataNetEmoji() {
            DataID = "netemoji";
        }

        public string ID = "";
        public bool FirstFragment, MoreFragments;
        public byte[] Data = Dummy<byte>.EmptyArray;

        protected override void Read(CelesteNetBinaryReader reader) {
            ID = reader.ReadNetString();
            Flags flags = (Flags) reader.ReadByte();
            FirstFragment = ((flags & Flags.FIRST) != 0);
            MoreFragments = ((flags & Flags.MORE) != 0);
            Data = reader.ReadBytes(reader.ReadInt32());
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(ID);
            writer.Write((byte) ((FirstFragment ? Flags.FIRST : 0) | (MoreFragments ? Flags.MORE : 0)));
            writer.Write(Data.Length);
            writer.Write(Data);
        }

    }
}
