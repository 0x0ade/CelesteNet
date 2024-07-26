using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPicoEnd : DataType<DataPicoEnd> {
        public DataPlayerInfo? Player;
        public bool KillPlayer;
        
        static DataPicoEnd() {
            DataID = "picoend";
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            Player = reader.ReadOptRef<DataPlayerInfo>();
            KillPlayer = reader.ReadBoolean();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteOptRef(Player);
            writer.Write(KillPlayer);
        }
    }
}