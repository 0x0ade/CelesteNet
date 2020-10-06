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

        public override DataFlags DataFlags => DataFlags.Taskable;

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
            => !Text.IsNullOrEmpty();

        public override bool FilterSend(DataContext ctx)
            => !Text.IsNullOrEmpty();

        public override void Read(CelesteNetBinaryReader reader) {
            CreatedByServer = false;
            Player = reader.ReadRef<DataPlayerInfo>();
            ID = reader.ReadUInt32();
            Tag = reader.ReadNetString();
            Text = reader.ReadNetString();
            Color = reader.ReadColorNoA();
            Date = reader.ReadDateTime();
            ReceivedDate = DateTime.UtcNow;
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteOptRef(Player);
            writer.Write(ID);
            writer.WriteNetString(Tag);
            writer.WriteNetString(Text);
            writer.WriteNoA(Color);
            writer.Write(Date);
        }

        public override string ToString()
            => ToString(true, false);

        public string ToString(bool displayName, bool id)
            => $"{(id ? $"{{{ID}}} " : "")}{(Tag.IsNullOrEmpty() ? "" : $"[{Tag}] ")}{(displayName ? Player?.DisplayName : Player?.FullName) ?? "**SERVER**"}{(Target != null ? " @ " + Target.DisplayName : "")}:{(Text.Contains('\n') ? "\n" : " ")}{Text}";


    }
}
