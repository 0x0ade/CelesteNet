﻿using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataHandshakeServer : DataType<DataHandshakeServer> {

        static DataHandshakeServer() {
            DataID = "hsS";
        }

        public ushort Version = CelesteNetUtils.Version;

#pragma warning disable CS8618 // You shouldn't be using an unintialized handshake anyway.
        public DataPlayerInfo PlayerInfo;
#pragma warning restore CS8618

        public override void Read(CelesteNetBinaryReader reader) {
            Version = reader.ReadUInt16();

            PlayerInfo = new DataPlayerInfo().ReadAllT(reader);
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write(Version);

            PlayerInfo.WriteAll(writer);
        }

        public override string ToString()
            => $"{Version}, {PlayerInfo}";

    }
}
