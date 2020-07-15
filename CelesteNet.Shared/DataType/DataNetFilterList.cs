using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataNetFilterList : DataType<DataNetFilterList> {

        static DataNetFilterList() {
            DataID = "filterList";
        }

        public override DataFlags DataFlags => DataFlags.Big;

        public DataPlayerInfo? Player;

        public string[] List = Dummy<string>.EmptyArray;
        private HashSet<string>? Set;

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaBoundRef(DataPlayerInfo.DataID, Player?.ID ?? uint.MaxValue, true)
            };

        public override void FixupMeta(DataContext ctx) {
            Player = ctx.GetRef<DataPlayerInfo>(Get<MetaBoundRef>(ctx).ID);
        }

        public override void Read(DataContext ctx, BinaryReader reader) {
            List = new string[reader.ReadUInt16()];
            for (int i = 0; i < List.Length; i++)
                List[i] = reader.ReadNetString();

            Set = new HashSet<string>(List);
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write((ushort) List.Length);
            foreach (string mod in List)
                writer.WriteNetString(mod);
        }

        public bool Contains(string mod)
            => Set?.Contains(mod) ?? List.Contains(mod);

    }
}
