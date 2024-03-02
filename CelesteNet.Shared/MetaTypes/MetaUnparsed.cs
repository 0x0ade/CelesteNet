using System;

namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class MetaUnparsed : MetaType<MetaUnparsed> {

        public string InnerID = "";
        public byte[] InnerData = Dummy<byte>.EmptyArray;

        public override string GetTypeID(DataContext ctx)
            => InnerID;

        public override void Read(CelesteNetBinaryReader reader) {
            throw new InvalidOperationException("Can't read unparsed MetaTypes");
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(InnerData);
        }

    }
}
