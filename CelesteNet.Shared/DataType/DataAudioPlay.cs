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
    public class DataAudioPlay : DataType<DataAudioPlay> {

        static DataAudioPlay() {
            DataID = "audioPlay";
        }

        public override DataFlags DataFlags => DataFlags.Update;

        public bool Server;
        public DataPlayerInfo? Player;

        public string Sound = "";
        public string Param = "";
        public float Value;

        public Vector2? Position;

        public override bool FilterHandle(DataContext ctx)
            => Server || Player != null; // Can be RECEIVED BY CLIENT TOO EARLY because UDP is UDP.

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerUpdate(Player)
            };

        public override void FixupMeta(DataContext ctx) {
            Player = Get<MetaPlayerUpdate>(ctx);
        }

        public override void Read(CelesteNetBinaryReader reader) {
            Server = reader.ReadBoolean();

            Sound = reader.ReadNetMappedString();
            Param = reader.ReadNetMappedString();
            if (!Param.IsNullOrEmpty())
                Value = reader.ReadSingle();

            if (reader.ReadBoolean())
                Position = reader.ReadVector2();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            if (Player != null)
                Server = false;

            writer.Write(Server);

            writer.WriteNetMappedString(Sound);
            writer.WriteNetMappedString(Param);
            if (!Param.IsNullOrEmpty())
                writer.Write(Value);

            if (Position == null) {
                writer.Write(false);
            } else {
                writer.Write(true);
                writer.Write(Position.Value);
            }
        }

    }
}
