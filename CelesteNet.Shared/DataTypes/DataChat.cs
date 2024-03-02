using Microsoft.Xna.Framework;
using System;

namespace Celeste.Mod.CelesteNet.DataTypes
{
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

        public uint ID = 0xffffff;
        public byte Version = 0;
        public string Tag = "";
        public string Text = "";
        public Color Color = Color.White;
        public DateTime Date = DateTime.UtcNow;

        public DateTime ReceivedDate = DateTime.UtcNow;

        public override bool FilterHandle(DataContext ctx)
            => !Text.IsNullOrEmpty();

        public override bool FilterSend(DataContext ctx)
            => !Text.IsNullOrEmpty();

        protected override void Read(CelesteNetBinaryReader reader) {
            CreatedByServer = false;
            Player = reader.ReadOptRef<DataPlayerInfo>();
            uint packedID = reader.ReadUInt32();
            ID = (packedID >> 0) & 0xffffff;
            Version = (byte) ((packedID >> 24) & 0xff);
            if (ID == 0xffffff) {
                ID = uint.MaxValue;
                Version = 0;
            }
            Tag = reader.ReadNetString();
            Text = reader.ReadNetString();
            Color = reader.ReadColorNoA();
            Date = reader.ReadDateTime();
            ReceivedDate = DateTime.UtcNow;
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteOptRef(Player);
            writer.Write(
                ((uint) (ID & 0xffffff) << 0) |
                ((uint) (Version & 0xff) << 24)
            );
            writer.WriteNetString(Tag);
            writer.WriteNetString(Text);
            writer.WriteNoA(Color);
            writer.Write(Date);
        }

        public override string ToString()
            => ToString(true, false);

        public string ToString(bool displayName, bool id)
            => $"{(id ? $"{{{ID}v{Version}}} " : "")}{(Tag.IsNullOrEmpty() ? "" : $"[{Tag}] ")}{(displayName ? Player?.DisplayName : Player?.FullName) ?? "**SERVER**"}{(Target != null ? " @ " + Target.DisplayName : "")}:{(Text.IndexOf('\n') != -1 ? "\n" : " ")}{Text}";


    }
}
