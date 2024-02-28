namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class DataLowLevelPingReply : DataType<DataLowLevelPingReply> {

        static DataLowLevelPingReply() {
            DataID = "pingReply";
        }

        public override DataFlags DataFlags => DataFlags.CoreType;

        public long PingTime;

        protected override void Read(CelesteNetBinaryReader reader) {
            PingTime = reader.ReadInt64();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(PingTime);
        }

    }
}
