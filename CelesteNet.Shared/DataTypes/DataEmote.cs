﻿using Microsoft.Xna.Framework;
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

        public override DataFlags DataFlags => DataFlags.Unreliable | DataFlags.CoreType;

        public DataPlayerInfo? Player;

        public string Text = "";

        public override bool FilterHandle(DataContext ctx)
            => Player != null && !Text.IsNullOrEmpty();

        public override bool FilterSend(DataContext ctx)
            => !Text.IsNullOrEmpty();

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerUpdate(Player)
            };

        public override void FixupMeta(DataContext ctx) {
            Player = Get<MetaPlayerUpdate>(ctx);
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            Text = reader.ReadNetString();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Text);
        }

        public override string ToString()
            => $"{Player}: {Text}";

    }
}
