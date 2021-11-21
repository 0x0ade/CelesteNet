using System;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataLowLevelKeepAlive : DataType<DataLowLevelKeepAlive> {

        static DataLowLevelKeepAlive() {
            DataID = "keepAlive";
        }

        public override DataFlags DataFlags => DataFlags.SlimHeader;

        protected override void Read(CelesteNetBinaryReader reader) {}
        protected override void Write(CelesteNetBinaryWriter writer) {}

    }
}