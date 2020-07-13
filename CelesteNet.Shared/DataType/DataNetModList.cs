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
    public class DataNetModList : DataType<DataNetModList> {

        static DataNetModList() {
            DataID = "netmods";
        }

        public override DataFlags DataFlags => DataFlags.Big;

        public string[] List = Dummy<string>.EmptyArray;

        // TODO: Bind to player?

        public override void Read(DataContext ctx, BinaryReader reader) {
            List = new string[reader.ReadUInt16()];
            for (int i = 0; i < List.Length; i++)
                List[i] = reader.ReadNetString();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.Write((ushort) List.Length);
            foreach (string mod in List)
                writer.WriteNetString(mod);
        }

    }
}
