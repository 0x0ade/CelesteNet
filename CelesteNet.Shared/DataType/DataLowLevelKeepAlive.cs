using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelKeepAlive : DataType<DataLowLevelStringMap> {

        static DataLowLevelKeepAlive() {
            DataID = "keepAlive";
        }

        public override DataFlags DataFlags => DataFlags.SlimHeader;

        public override void Read(CelesteNetBinaryReader reader) {}
        public override void Write(CelesteNetBinaryWriter writer) {}

    }
}