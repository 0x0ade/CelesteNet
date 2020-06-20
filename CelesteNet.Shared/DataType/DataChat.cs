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
    public class DataChat : DataType<DataChat> {

        static DataChat() {
            DataID = "chat";
        }

        /// <summary>
        /// Server-internal field.
        /// </summary>
        public bool CreatedByServer = true;
        /// <summary>
        /// Server-internal field.
        /// </summary>
        public DataPlayerInfo[]? Targets;
        /// <summary>
        /// Server-internal field.
        /// </summary>
        public DataPlayerInfo? Target {
            get => Targets != null && Targets.Length == 1 ? Targets[0] : null;
            set => Targets = value == null ? null : new DataPlayerInfo[] { value };
        }

        public DataPlayerInfo? Player;

        public uint ID = uint.MaxValue;
        public string Tag = "";
        public string Text = "";
        public Color Color = Color.White;
        public DateTime Date = DateTime.UtcNow;

        public DateTime ReceivedDate = DateTime.UtcNow;

        public override bool FilterHandle(DataContext ctx)
            => !string.IsNullOrEmpty(Text);

        public override bool FilterSend(DataContext ctx)
            => !string.IsNullOrEmpty(Text);

        public override void Read(DataContext ctx, BinaryReader reader) {
            CreatedByServer = false;
            Player = ctx.ReadRef<DataPlayerInfo>(reader);
            ID = reader.ReadUInt32();
            Tag = reader.ReadNullTerminatedString();
            Text = reader.ReadNullTerminatedString();
            Color = new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);
            Date = DateTime.FromBinary(reader.ReadInt64());
            ReceivedDate = DateTime.UtcNow;
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            ctx.WriteRef(writer, Player);
            writer.Write(ID);
            writer.WriteNullTerminatedString(Tag);
            writer.WriteNullTerminatedString(Text);
            writer.Write(Color.R);
            writer.Write(Color.G);
            writer.Write(Color.B);
            writer.Write(Date.ToBinary());
        }

        public override string ToString()
            => $"[{Date.ToLocalTime().ToLongTimeString()}]{(string.IsNullOrEmpty(Tag) ? "" : $"[{Tag}]")} {Player?.FullName ?? "**SERVER**"}{(Target != null ? " @ " + Target.FullName : "")}:{(Text.Contains('\n') ? "\n" : " ")}{Text}";

    }
}
