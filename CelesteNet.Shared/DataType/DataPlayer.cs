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
    [DataReference]
    public class DataPlayer : DataType<DataPlayer> {

        static DataPlayer() {
            DataID = "playerInfo";
            DataFlags = DataFlags.None;
        }

        public override bool IsValid => true;

        public uint ID;
        public string Name;
        public string FullName;

        public override void Read(BinaryReader reader) {
            ID = reader.ReadUInt32();
            Name = reader.ReadNullTerminatedString();
            FullName = reader.ReadNullTerminatedString();
        }

        public override void Write(BinaryWriter writer) {
            writer.Write(ID);
            writer.WriteNullTerminatedString(Name);
            writer.WriteNullTerminatedString(FullName);
        }

        public override DataPlayer CloneT()
            => new DataPlayer {
                ID = ID,
                Name = Name,
                FullName = FullName
            };

        public override string ToString()
            => $"{FullName} ({Name}#{ID})";

    }
}
