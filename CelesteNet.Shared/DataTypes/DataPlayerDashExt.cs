using Microsoft.Xna.Framework;
using Monocle;
using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPlayerDashExt : DataType<DataPlayerDashExt> {
        public DataPlayerInfo? Player;
        public int Dashes;
        public Color P_DashColor;
        public Color P_DashColor2;

        static DataPlayerDashExt() {
            DataID = "playerDashExt";
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
            P_DashColor = reader.ReadColor();
            P_DashColor2 = reader.ReadColor();

            Meta = GenerateMeta(reader.Data);
        }

        public override void WriteAll(CelesteNetBinaryWriter writer) {
            FixupMeta(writer.Data);
            writer.WriteRef(Player);

            writer.Write7BitEncodedInt(Dashes);
            writer.Write(P_DashColor);
            writer.Write(P_DashColor2);
        }
    }
}
