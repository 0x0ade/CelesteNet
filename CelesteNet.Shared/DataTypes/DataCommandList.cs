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
    public class DataCommandList : DataType<DataCommandList> {

        static DataCommandList() {
            DataID = "commandList";
        }

        public Command[] List = Dummy<Command>.EmptyArray;

        protected override void Read(CelesteNetBinaryReader reader) {
            List = new Command[reader.ReadUInt32()];
            for (int ci = 0; ci < List.Length; ci++) {
                Command c = List[ci] = new();
                c.ID = reader.ReadNetString();
            }
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write((uint) List.Length);
            foreach (Command c in List) {
                writer.WriteNetString(c.ID);
            }
        }

        public class Command {
            public string ID = "";
        }

    }
}
