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
    public class DataChannelList : DataType<DataChannelList> {

        static DataChannelList() {
            DataID = "channelList";
        }

        public Channel[] List = Dummy<Channel>.EmptyArray;

        protected override void Read(CelesteNetBinaryReader reader) {
            List = new Channel[reader.ReadUInt32()];
            for (int ci = 0; ci < List.Length; ci++) {
                Channel c = List[ci] = new();
                c.Name = reader.ReadNetString();
                c.ID = reader.ReadUInt32();
                c.Players = new uint[reader.ReadUInt32()];
                for (int pi = 0; pi < c.Players.Length; pi++)
                    c.Players[pi] = reader.ReadUInt32();
            }
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write((uint) List.Length);
            foreach (Channel c in List) {
                writer.WriteNetString(c.Name);
                writer.Write(c.ID);
                writer.Write((uint) c.Players.Length);
                foreach (uint p in c.Players)
                    writer.Write(p);
            }
        }

        public class Channel {
            public string Name = "";
            public uint ID;
            public uint[] Players = Dummy<uint>.EmptyArray;
        }

    }
}
