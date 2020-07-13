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
    public class DataPlayerState : DataType<DataPlayerState> {

        static DataPlayerState() {
            DataID = "playerState";
        }

        public DataPlayerInfo? Player;

        public string SID = "";
        public AreaMode Mode;
        public string Level = "";
        public bool Idle;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerPrivateState(Player),
                new MetaBoundRef(DataPlayerInfo.DataID, Player?.ID ?? uint.MaxValue, true)
            };

        public override void FixupMeta(DataContext ctx) {
            Player = Get<MetaPlayerPrivateState>(ctx);
            Get<MetaBoundRef>(ctx).ID = Player?.ID ?? uint.MaxValue;
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
            SID = reader.ReadNetString();
            Mode = (AreaMode) reader.ReadByte();
            Level = reader.ReadNetString();
            Idle = reader.ReadBoolean();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNetString(SID);
            writer.Write((byte) Mode);
            writer.WriteNetString(Level);
            writer.Write(Idle);
        }

        public override string ToString()
            => $"#{Player?.ID ?? uint.MaxValue}: {SID}, {Idle}";

    }
}
