using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelSlimMap : DataType<DataLowLevelStringMap> {

        static DataLowLevelSlimMap() {
            DataID = "slimMap";
        }

        public override DataFlags DataFlags => DataFlags.SlimHeader;

        public Type? PacketType = null;
        public int ID;

        public override void Read(CelesteNetBinaryReader reader) {
            PacketType = Type.GetType(reader.ReadNetString(), false);
            ID = reader.ReadInt32();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(PacketType?.FullName);
            writer.Write(ID);
        }

    }
}