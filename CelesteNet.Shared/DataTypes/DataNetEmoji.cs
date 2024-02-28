namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class DataNetEmoji : DataType<DataNetEmoji> {

        static DataNetEmoji() {
            DataID = "netemoji";
        }

        public const int MaxSequenceNumber = 128;

        public string ID = "";
        public int SequenceNumber;
        public bool MoreFragments;
        public byte[] Data = Dummy<byte>.EmptyArray;

        protected override void Read(CelesteNetBinaryReader reader) {
            ID = reader.ReadNetString();
            byte header = reader.ReadByte();
            SequenceNumber = header >> 2;
            MoreFragments = (header & 0b10) != 0;
            Data = reader.ReadBytes(reader.ReadInt32());
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            byte header = 0;
            if (SequenceNumber == 0)
                // Backwards compat with old clients. This means that emojis can be a maximum of 64kb before things break
                header |= 0b01;
            if (MoreFragments)
                header |= 0b10;
            header |= (byte) ((SequenceNumber % MaxSequenceNumber) << 2);

            writer.WriteNetString(ID);
            writer.Write(header);
            writer.Write(Data.Length);
            writer.Write(Data);
        }

    }
}
