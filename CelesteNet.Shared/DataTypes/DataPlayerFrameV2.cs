using Microsoft.Xna.Framework;
using Monocle;
using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPlayerFrameV2 : DataType<DataPlayerFrameV2> {
        public DataPlayerInfo? Player;
        public int Dashes;

        static DataPlayerFrameV2() {
            DataID = "playerFrameV2";
        }

        public override bool FilterHandle(DataContext ctx)
            => Player != null;

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
            Dashes = reader.Read7BitEncodedInt();

            Meta = GenerateMeta(reader.Data);
        }

        public override void WriteAll(CelesteNetBinaryWriter writer) {
            FixupMeta(writer.Data);
            writer.WriteRef(Player);

            writer.Write7BitEncodedInt(Dashes);
        }
    }
}
