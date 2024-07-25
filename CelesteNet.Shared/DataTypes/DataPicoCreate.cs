using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPicoCreate : DataType<DataPicoCreate> {
        public uint ID;

        static DataPicoCreate() {
            DataID = "picocreate";
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            ID = reader.ReadUInt32();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(ID);
        }
    }
}