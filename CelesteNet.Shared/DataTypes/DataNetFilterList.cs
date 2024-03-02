using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.DataTypes
{
    public class DataNetFilterList : DataType<DataNetFilterList> {

        static DataNetFilterList() {
            DataID = "filterList";
        }

        public override DataFlags DataFlags => DataFlags.Taskable;

        public string[] List = Dummy<string>.EmptyArray;
        private HashSet<string>? Set;

        protected override void Read(CelesteNetBinaryReader reader) {
            List = new string[reader.ReadUInt16()];
            for (int i = 0; i < List.Length; i++)
                List[i] = reader.ReadNetString();

            Set = new(List, StringComparer.OrdinalIgnoreCase);
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write((ushort) List.Length);
            foreach (string mod in List)
                writer.WriteNetString(mod);
        }

        public bool Contains(string mod) {
            if (Set != null) {
                lock (Set)
                    return Set.Contains(mod);
            }

            foreach (string other in List)
                if (other.Equals(mod, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

    }
}
