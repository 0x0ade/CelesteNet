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
    public class DataPlayerInfo : DataType<DataPlayerInfo>, IDataRef {

        static DataPlayerInfo() {
            DataID = "playerInfo";
        }

        public uint ID { get; set; }
        public bool IsAliveRef => !string.IsNullOrEmpty(FullName);
        public string Name = "";
        public string FullName = "";
        public string DisplayName = "";

        public override void Read(DataContext ctx, BinaryReader reader) {
            ID = reader.ReadUInt32();
            Name = reader.ReadNullTerminatedString();
            FullName = reader.ReadNullTerminatedString();
            DisplayName = reader.ReadNullTerminatedString();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write(ID);
            writer.WriteNullTerminatedString(Name);
            writer.WriteNullTerminatedString(FullName);
            writer.WriteNullTerminatedString(DisplayName);
        }

        public override string ToString()
            => $"#{ID}: {FullName} ({Name})";

    }

    [DataBehavior("playerPubState")]
    public interface IDataPlayerPublicState : IDataBoundRef<DataPlayerInfo> {

        DataPlayerInfo? Player { get; set; }

    }

    [DataBehavior("playerState")]
    public interface IDataPlayerState : IDataBoundRef<DataPlayerInfo> {

        DataPlayerInfo? Player { get; set; }

    }

    [DataBehavior("playerUpdate")]
    public interface IDataPlayerUpdate {

        DataPlayerInfo? Player { get; set; }

    }
}
