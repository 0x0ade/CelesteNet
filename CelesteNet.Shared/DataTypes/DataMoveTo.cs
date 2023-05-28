using Celeste.Mod.CelesteNet.MonocleCelesteHelpers;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataMoveTo : DataType<DataMoveTo> {

        static DataMoveTo() {
            DataID = "playerMoveTo";
        }

        public bool Force;
        public string SID = "";
        public AreaMode Mode;
        public string Level = "";

        public DataSession? Session;

        public Vector2? Position;

        protected override void Read(CelesteNetBinaryReader reader) {
            Force = reader.ReadBoolean();
            SID = reader.ReadNetString();
            Mode = (AreaMode) reader.ReadByte();
            Level = reader.ReadNetString();

            if (reader.ReadBoolean()) {
                Session = new DataSession();
                Session.ReadAll(reader);
            } else {
                Session = null;
            }

            if (reader.ReadBoolean())
                Position = reader.ReadVector2();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(Force);
            writer.WriteNetString(SID);
            writer.Write((byte) Mode);
            writer.WriteNetString(Level);

            if (Session == null) {
                writer.Write(false);
            } else {
                writer.Write(true);
                Session.WriteAll(writer);
            }

            if (Position == null) {
                writer.Write(false);
            } else {
                writer.Write(true);
                writer.Write(Position.Value);
            }
        }

    }
}
