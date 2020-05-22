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

namespace Celeste.Mod.CelesteNet.Shared.DataTypes {
    public class DataChat : DataType<DataChat> {

        static DataChat() {
            ChunkID = "chat";
        }

        public override bool IsValid => !string.IsNullOrWhiteSpace(Text);

        /// <summary>
        /// Server-internal field.
        /// </summary>
        public bool CreatedByServer;
        /// <summary>
        /// Server-internal field.
        /// </summary>
        public bool Logged;

        public uint ID;
        public string Tag;
        public string Text;
        public Color Color;
        public DateTime Date;

        public override void Read(BinaryReader reader) {
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

        public override object Clone()
            => new DataChat {
                CreatedByServer = CreatedByServer,
                Logged = Logged,

                ID = ID,
                Tag = Tag,
                Text = Text,
                Color = Color,
                Date = Date
            };

    }
}
