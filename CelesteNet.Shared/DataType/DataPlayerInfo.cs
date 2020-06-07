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
    public class DataPlayerInfo : DataType<DataPlayerInfo>, IDataRefType {

        static DataPlayerInfo() {
            DataID = "playerInfo";
        }

        public uint ID { get; set; }
        public bool IsAliveRef => !string.IsNullOrEmpty(FullName);
        public string Name;
        public string FullName;

        public override void Read(DataContext ctx, BinaryReader reader) {
            ID = reader.ReadUInt32();
            Name = reader.ReadNullTerminatedString();
            FullName = reader.ReadNullTerminatedString();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write(ID);
            writer.WriteNullTerminatedString(Name);
            writer.WriteNullTerminatedString(FullName);
        }

        public override string ToString()
            => $"#{ID}: {FullName} ({Name})";

    }

    public interface IDataPlayerUpdate {

        DataPlayerInfo Player { get; set; }

    }
}
