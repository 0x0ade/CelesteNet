using System;
using Microsoft.Xna.Framework;

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
        [Obsolete("Use TryGetSingleTarget instead of this getter, do `Targets = [value]` instead of this setter.", false)]
        public DataPlayerInfo? Target {
            get {
                if (Targets != null && Targets.Length == 1) {
                    return Targets[0];
                } else {
                    return null;
                }
            }

            set {
                if (value == null) {
                    Targets = null;
                } else {
                    Targets = new DataPlayerInfo[] { value };
                }
            }
        }

        public bool TryGetSingleTarget(out DataPlayerInfo? target) {
            if (Targets != null && Targets.Length == 1) {
                target = Targets[0];
                return true;
            }

            target = null;
            return false;
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


        public string ToString(bool useDisplayName, bool withID) {
            string id = "";
            if (withID)
                id = $"{{{ID}v{Version}}}";

            string tag = "";
            if (!Tag.IsNullOrEmpty())
                tag = $"[{Tag}]";

            string? username = useDisplayName ? Player?.DisplayName : Player?.FullName;
            username ??= "**SERVER**";

            if (TryGetSingleTarget(out DataPlayerInfo? target) && target != null)
                username += " @ " + target.DisplayName;

            string prefix = $"{id} {tag} {username}:".TrimStart();

            if (Text.IndexOf('\n') == -1) {
                return $"{prefix} {Text}";
            } else {
                return $"{prefix}\n{Text}";
            }
        }
    }
}
