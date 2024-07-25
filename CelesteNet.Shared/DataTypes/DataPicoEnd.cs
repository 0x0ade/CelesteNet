using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPicoEnd : DataType<DataPicoCreate> {
        public uint ID;
        
        static DataPicoEnd() {
            DataID = "picoend";
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            ID = reader.ReadUInt32();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(ID);
        }
    }
}