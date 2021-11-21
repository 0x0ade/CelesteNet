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
    public class DataPlayerGrabPlayer : DataType<DataPlayerGrabPlayer> {

        static DataPlayerGrabPlayer() {
            DataID = "playerGrabPlayer";
        }

        // Too many too quickly to make tasking worth it.
        public override DataFlags DataFlags => DataFlags.Unreliable | DataFlags.SlimHeader | DataFlags.Small;

        public DataPlayerInfo? Player;

        public DataPlayerInfo? Grabbing;
        public Vector2 Position;
        public Vector2? Force;

        public override bool FilterHandle(DataContext ctx)
            => Player != null && Grabbing != null; // Can be RECEIVED BY CLIENT TOO EARLY because UDP is UDP.

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerUpdate(Player),
                new MetaOrderedUpdate(Player?.ID ?? uint.MaxValue)
            };

        public override void FixupMeta(DataContext ctx) {
            MetaPlayerUpdate playerUpd = Get<MetaPlayerUpdate>(ctx);
            MetaOrderedUpdate order = Get<MetaOrderedUpdate>(ctx);

            order.ID = playerUpd;
            Player = playerUpd;
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            Grabbing = reader.ReadOptRef<DataPlayerInfo>();

            Position = reader.ReadVector2();

            if (reader.ReadBoolean())
                Force = reader.ReadVector2();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteRef(Grabbing);

            writer.Write(Position);

            if (Force == null) {
                writer.Write(false);
            } else {
                writer.Write(true);
                writer.Write(Force.Value);
            }
        }

    }
}
