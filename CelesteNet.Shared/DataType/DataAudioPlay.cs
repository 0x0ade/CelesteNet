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
    public class DataAudioPlay : DataType<DataAudioPlay>, IDataPlayerUpdate {

        static DataAudioPlay() {
            DataID = "audioPlay";
        }

        public override DataFlags DataFlags => DataFlags.Update;

        public bool Server;
        public DataPlayerInfo? Player { get; set; }

        public string Sound = "";
        public string Param = "";
        public float Value;

        public Vector2? Position;

        public override bool FilterHandle(DataContext ctx)
            => Server || Player != null; // Can be RECEIVED BY CLIENT TOO EARLY because UDP is UDP.

        public override void Read(DataContext ctx, BinaryReader reader) {
            if (!(Server = reader.ReadBoolean()))
                Player = ctx.ReadOptRef<DataPlayerInfo>(reader);

            Sound = reader.ReadNullTerminatedString();
            Param = reader.ReadNullTerminatedString();
            if (!string.IsNullOrEmpty(Param))
                Value = reader.ReadSingle();

            if (reader.ReadBoolean())
                Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write(Server);
            if (!Server)
                ctx.WriteRef(writer, Player);

            writer.WriteNullTerminatedString(Sound);
            writer.WriteNullTerminatedString(Param);
            if (!string.IsNullOrEmpty(Param))
                writer.Write(Value);

            if (Position == null) {
                writer.Write(false);
            } else {
                writer.Write(true);
                writer.Write(Position.Value.X);
                writer.Write(Position.Value.Y);
            }
        }

    }
}
