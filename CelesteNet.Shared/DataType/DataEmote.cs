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
    public class DataEmote : DataType<DataEmote> {

        static DataEmote() {
            DataID = "emote";
        }

        public DataPlayerInfo? Player;

        public string Text = "";

        public override bool FilterHandle(DataContext ctx)
            => Player != null && !string.IsNullOrEmpty(Text);

        public override bool FilterSend(DataContext ctx)
            => !string.IsNullOrEmpty(Text);

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerUpdate(Player)
            };

        public override void FixupMeta(DataContext ctx) {
            Player = Get<MetaPlayerUpdate>(ctx);
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
            Text = reader.ReadNullTerminatedString();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNullTerminatedString(Text);
        }

        public override string ToString()
            => $"{Player}: {Text}";

    }
}
