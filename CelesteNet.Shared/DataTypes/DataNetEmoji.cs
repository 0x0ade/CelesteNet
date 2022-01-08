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

        public const int MaxSequenceNumber = 128;

        public string ID = "";
        public int SequenceNumber;
        public bool MoreFragments;
        public byte[] Data = Dummy<byte>.EmptyArray;

        protected override void Read(CelesteNetBinaryReader reader) {
            // This bitmagic encoding is needed to support old versions
            // Because this causes the sequence number 0 to be read as Flags.FIRST
            ID = reader.ReadNetString();
            byte header = reader.ReadByte();
            SequenceNumber = unchecked((byte) ((header >> 1) - 1)) % MaxSequenceNumber;
            MoreFragments = (header & 0b1) != 0;
            Data = reader.ReadBytes(reader.ReadInt32());
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(ID);
            writer.Write((byte) ((((SequenceNumber % MaxSequenceNumber) + 1) << 1) | (MoreFragments ? 0b1 : 0b0)));
            writer.Write(Data.Length);
            writer.Write(Data);
        }

    }
}
