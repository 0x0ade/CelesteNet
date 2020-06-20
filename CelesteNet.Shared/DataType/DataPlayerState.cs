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
    public class DataPlayerState : DataType<DataPlayerState>, IDataPlayerState {

        static DataPlayerState() {
            DataID = "playerState";
        }

        public DataPlayerInfo? Player { get; set; }
        public uint ID => Player?.ID ?? uint.MaxValue;
        public bool IsAliveRef => true;

        public string SID = "";
        public AreaMode Mode;
        public string Level = "";
        public bool Idle;

        public override void Read(DataContext ctx, BinaryReader reader) {
            Player = ctx.ReadRef<DataPlayerInfo>(reader);
            SID = reader.ReadNullTerminatedString();
            Mode = (AreaMode) reader.ReadByte();
            Level = reader.ReadNullTerminatedString();
            Idle = reader.ReadBoolean();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            ctx.WriteRef(writer, Player);
            writer.WriteNullTerminatedString(SID);
            writer.Write((byte) Mode);
            writer.WriteNullTerminatedString(Level);
            writer.Write(Idle);
        }

        public override string ToString()
            => $"#{ID}: {SID}, {Idle}";

    }
}
