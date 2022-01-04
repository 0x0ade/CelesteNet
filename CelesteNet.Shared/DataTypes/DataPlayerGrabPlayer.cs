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

        public const byte MaxGrabStrength = 1 << 7 - 1;

        static DataPlayerGrabPlayer() {
            DataID = "playerGrabPlayer";
        }

        // Too many too quickly to make tasking worth it.
        public override DataFlags DataFlags => DataFlags.Unreliable | DataFlags.CoreType | DataFlags.Small | DataFlags.NoStandardMeta;

        public DataPlayerInfo? Player, Grabbing;
        public Vector2 Position;
        public Vector2? Force;
        public byte GrabStrength;

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

        public override void ReadAll(CelesteNetBinaryReader reader) {
            Player = reader.ReadRef<DataPlayerInfo>();
            Grabbing = reader.ReadOptRef<DataPlayerInfo>();
            Position = reader.ReadVector2();

            byte forceStrength = reader.ReadByte();
            Force = ((forceStrength & 0b1) != 0) ? reader.ReadVector2() : null;
            GrabStrength = (byte) (forceStrength >> 1);

            Meta = GenerateMeta(reader.Data);
        }

        public override void WriteAll(CelesteNetBinaryWriter writer) {
            FixupMeta(writer.Data);

            writer.WriteRef(Player);
            writer.WriteOptRef(Grabbing);
            writer.Write(Position);

            byte forceStrength = unchecked((byte) (GrabStrength << 1));
            if (Force.HasValue)
                forceStrength |= 0b1;
            writer.Write(forceStrength);
            if ((forceStrength & 0b1) != 0)
                writer.Write(Force!.Value);
        }

    }
}
