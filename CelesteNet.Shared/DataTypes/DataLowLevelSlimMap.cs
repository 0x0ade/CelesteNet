using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelSlimMap : DataType<DataLowLevelSlimMap> {

        static DataLowLevelSlimMap() {
            DataID = "slimMap";
        }

        public override DataFlags DataFlags => DataFlags.SlimHeader;

        public Type? PacketType = null;
        public int ID;

        protected override void Read(CelesteNetBinaryReader reader) {
            PacketType = Type.GetType(reader.ReadNetString(), false);
            ID = reader.ReadInt32();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(PacketType?.FullName);
            writer.Write(ID);
        }

    }
}