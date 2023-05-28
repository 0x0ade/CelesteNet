namespace Celeste.Mod.CelesteNet.DataTypes {
    public class MetaBoundRef : MetaType<MetaBoundRef> {

        static MetaBoundRef() {
            MetaID = "bound";
        }

        public string TypeBoundTo = "";
        public uint ID;
        public bool IsAlive;

        public MetaBoundRef() {
        }
        public MetaBoundRef(string typeBoundTo, uint id, bool isAlive) {
            TypeBoundTo = typeBoundTo;
            ID = id;
            IsAlive = isAlive;
        }

        public override void Read(CelesteNetBinaryReader reader) {
            TypeBoundTo = reader.ReadNetMappedString();
            ID = reader.Read7BitEncodedUInt();
            IsAlive = reader.ReadBoolean();
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetMappedString(TypeBoundTo);
            writer.Write7BitEncodedUInt(ID);
            writer.Write(IsAlive);
        }

    }
}
