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

        public CommandInfo[] List = Dummy<CommandInfo>.EmptyArray;

        protected override void Read(CelesteNetBinaryReader reader) {
            List = new CommandInfo[reader.ReadUInt32()];
            for (int ci = 0; ci < List.Length; ci++) {
                CommandInfo c = List[ci] = new();
                c.ID = reader.ReadNetString();
                c.AliasTo = reader.ReadNetString();
                c.Auth = reader.ReadBoolean();
                c.AuthExec = reader.ReadBoolean();
                c.FirstArg = (CompletionType) reader.ReadByte();
            }
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write((uint) List.Length);
            foreach (CommandInfo c in List) {
                writer.WriteNetString(c.ID);
                writer.WriteNetString(c.AliasTo);
                writer.Write(c.Auth);
                writer.Write(c.AuthExec);
                writer.Write((byte) c.FirstArg);
            }
        }
    }
}
