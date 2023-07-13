namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataUnparsed : DataType<DataUnparsed> {

        public override DataFlags DataFlags => InnerFlags & ~DataFlags.Small;

        public string InnerID = "";
        public string InnerSource = "";
        public DataFlags InnerFlags;
        public int InnerLength;
        private byte[] InnerData = Dummy<byte>.EmptyArray;

        public override string GetTypeID(DataContext ctx)
            => InnerID;

        public override string GetSource(DataContext ctx)
            => InnerSource;


        public override void ReadAll(CelesteNetBinaryReader reader) {
            long metaStart = reader.BaseStream.Position;
            if ((InnerFlags & DataFlags.NoStandardMeta) == 0)
                Meta = ReadMeta(reader);
            int metaLength = (int) (reader.BaseStream.Position - metaStart);
            InnerData = reader.ReadBytes(InnerLength - metaLength);
        }

        public override void WriteAll(CelesteNetBinaryWriter writer) {
            if ((InnerFlags & DataFlags.NoStandardMeta) == 0)
                WriteMeta(writer, Meta);
            writer.Write(InnerData);
        }

    }
}
