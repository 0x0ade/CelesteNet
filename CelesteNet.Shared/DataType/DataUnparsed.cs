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
    public class DataUnparsed : DataType<DataUnparsed> {

        static DataUnparsed() {
            DataID = "unparsed";
        }

        public override DataFlags DataFlags => InnerFlags & (DataFlags.Update);

        public string InnerID = "";
        public string InnerSource = "";
        public DataFlags InnerFlags;
        public string[] Behaviors = Dummy<string>.EmptyArray;
        public byte[] InnerData = Dummy<byte>.EmptyArray;

        public override void Read(DataContext ctx, BinaryReader reader) {
            InnerID = reader.ReadNullTerminatedString();
            InnerSource = reader.ReadNullTerminatedString();
            InnerFlags = (DataFlags) reader.ReadUInt16();
            Behaviors = new string[reader.ReadByte()];

            for (int i = 0; i < Behaviors.Length; i++)
                Behaviors[i] = reader.ReadNullTerminatedString();

            InnerData = reader.ReadBytes(reader.ReadUInt16());
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNullTerminatedString(InnerID);
            writer.WriteNullTerminatedString(InnerSource);
            writer.Write((ushort) InnerFlags);

            writer.Write((byte) Behaviors.Length);
            foreach (string behavior in Behaviors)
                writer.WriteNullTerminatedString(behavior);

            writer.Write((ushort) InnerData.Length);
            writer.Write(InnerData);
        }

        public bool HasBehavior<T>(DataContext ctx) {
            if (!ctx.TypeToBehaviorMap.TryGetValue(typeof(T), out string? behavior))
                return false;
            return Behaviors.Contains(behavior);
        }

    }
}
