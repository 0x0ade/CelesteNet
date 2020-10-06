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
    public class DataHandshakeTCPUDPClient : DataHandshakeClient<DataHandshakeTCPUDPClient> {

        static DataHandshakeTCPUDPClient() {
            DataID = "hsTUC";
        }

        public string[] ConnectionFeatures = Dummy<string>.EmptyArray;
        public uint ConnectionToken;

        public override void Read(CelesteNetBinaryReader reader) {
            base.Read(reader);

            ConnectionToken = reader.ReadUInt32();

            // FIXME: Remove this check with the next protocol version.
            if (ConnectionToken == uint.MaxValue) {
                ConnectionFeatures = new string[reader.ReadUInt16()];
                for (int i = 0; i < ConnectionFeatures.Length; i++)
                    ConnectionFeatures[i] = reader.ReadNetString();

                ConnectionToken = reader.ReadUInt32();
            }
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            base.Write(writer);

            // FIXME: Remove this check with the next protocol version.
            if (ConnectionFeatures.Length == 0) {
                writer.Write(ConnectionToken);
            } else {
                writer.Write(uint.MaxValue);

                writer.Write((ushort) ConnectionFeatures.Length);
                foreach (string feature in ConnectionFeatures)
                    writer.WriteNetString(feature);

                writer.Write(ConnectionToken);
            }
        }

    }
}
