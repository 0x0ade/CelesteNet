using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelSlimMap : DataType<DataLowLevelStringMap> {

        static DataLowLevelSlimMap() {
            DataID = "slimMap";
        }

        public Type PacketType = null!;
        public int ID;

        public override void Read(CelesteNetBinaryReader reader) {
            PacketType = Type.GetType(reader.ReadNetString(), false);
            ID = reader.ReadInt32();
        }

        override public void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(PacketType.FullName);
            writer.Write(ID);
        }

    }
}