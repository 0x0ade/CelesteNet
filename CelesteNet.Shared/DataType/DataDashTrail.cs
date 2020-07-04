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
    public class DataDashTrail : DataType<DataDashTrail> {

        static DataDashTrail() {
            DataID = "dashTrail";
        }

        public override DataFlags DataFlags => DataFlags.Update;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerUpdate(Player)
            };

        public bool Server;
        public DataPlayerInfo? Player;

        public Vector2 Position;
        public Vector2 Scale;
        public Color Color;

        public DataPartImage? Sprite;
        public int Depth;
        public float Duration;
        public bool FrozenUpdate;
        public bool UseRawDeltaTime;

        public override bool FilterHandle(DataContext ctx)
            => (Server || Player != null) && !(Server && Sprite == null); // Can be RECEIVED BY CLIENT TOO EARLY because UDP is UDP.

        public override void FixupMeta(DataContext ctx) {
            Player = Get<MetaPlayerUpdate>(ctx).Opt;
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
            Server = reader.ReadBoolean();

            Position = reader.ReadVector2();
            Scale = reader.ReadVector2();
            Color = reader.ReadColor();

            if (Server) {
                Sprite = new DataPartImage();
                Sprite.Read(ctx, reader);
                Depth = reader.ReadInt32();
                Duration = reader.ReadSingle();
                FrozenUpdate = reader.ReadBoolean();
                UseRawDeltaTime = reader.ReadBoolean();

            } else {
                Sprite = null;
                Depth = default;
                Duration = default;
                FrozenUpdate = default;
                UseRawDeltaTime = default;
            }
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            if (Player != null || Sprite == null)
                Server = false;

            writer.Write(Server);

            writer.Write(Position);
            writer.Write(Scale);
            writer.Write(Color);

            if (Server) {
                Sprite?.Write(ctx, writer);
                writer.Write(Depth);
                writer.Write(Duration);
                writer.Write(FrozenUpdate);
                writer.Write(UseRawDeltaTime);
            }
        }

    }
}
