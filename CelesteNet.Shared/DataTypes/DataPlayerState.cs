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

        public override DataFlags DataFlags => DataFlags.Taskable;

        public DataPlayerInfo? Player;

        public string SID = "";
        public AreaMode Mode;
        public string Level = "";
        public bool Idle;
        public bool Interactive;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerPrivateState(Player),
                new MetaBoundRef(DataPlayerInfo.DataID, Player?.ID ?? uint.MaxValue, true)
            };

        public override void FixupMeta(DataContext ctx) {
            Player = Get<MetaPlayerPrivateState>(ctx);
            Get<MetaBoundRef>(ctx).ID = Player?.ID ?? uint.MaxValue;
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            SID = reader.ReadNetString();
            Mode = (AreaMode) reader.ReadByte();
            Level = reader.ReadNetString();
            Idle = reader.ReadBoolean();
            Interactive = reader.ReadBoolean();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(SID);
            writer.Write((byte) Mode);
            writer.WriteNetString(Level);
            writer.Write(Idle);
            writer.Write(Interactive);
        }

        public override string ToString()
            => $"#{Player?.ID ?? uint.MaxValue}: {SID}, {Idle}";

    }
}
