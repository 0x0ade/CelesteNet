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

        [DataReference]
        public DataPlayer Player;

        public uint ID;
        public string Tag;
        public string Text;
        public Color Color;
        public DateTime Date;

        public override void Read(BinaryReader reader) {
            CreatedByServer = false;
            ID = reader.ReadUInt32();
            Tag = reader.ReadNullTerminatedString();
            Text = reader.ReadNullTerminatedString();
            Color = new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);
            Date = DateTime.FromBinary(reader.ReadInt64());
        }

        public override void Write(BinaryWriter writer) {
            writer.Write(ID);
            writer.WriteNullTerminatedString(Tag);
            writer.WriteNullTerminatedString(Text);
            writer.Write(Color.R);
            writer.Write(Color.G);
            writer.Write(Color.B);
            writer.Write(Date.ToBinary());
        }

        public override DataChat CloneT()
            => new DataChat {
                CreatedByServer = CreatedByServer,

                Player = Player,

                ID = ID,
                Tag = Tag,
                Text = Text,
                Color = Color,
                Date = Date
            };

        public override string ToString()
            => $"[{Date.ToLocalTime().ToLongTimeString()}]{(string.IsNullOrEmpty(Tag) ? "" : $"[{Tag}]")} {Player?.FullName ?? "**SERVER**"}:{(Text.Contains('\n') ? "\n" : " ")}{Text}";

    }
}
