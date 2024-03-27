namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class MetaRef : MetaType<MetaRef> {

        static MetaRef() {
            MetaID = "ref";
        }

        public uint ID;
        public bool IsAlive;

        public MetaRef() {
        }
        public MetaRef(uint id, bool isAlive) {
            ID = id;
            IsAlive = isAlive;
        }

        public override void Read(CelesteNetBinaryReader reader) {
            ID = reader.Read7BitEncodedUInt();
            IsAlive = reader.ReadBoolean();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedUInt(ID);
            writer.Write(IsAlive);
        }

        public static implicit operator uint(MetaRef meta)
            => meta.ID;

    }
}
