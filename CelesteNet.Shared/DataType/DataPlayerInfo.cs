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
    public class DataPlayerInfo : DataType<DataPlayerInfo> {

        static DataPlayerInfo() {
            DataID = "playerInfo";
        }

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRef(ID, !string.IsNullOrEmpty(FullName))
            };

        public uint ID;
        public string Name = "";
        public string FullName = "";
        public string DisplayName = "";

        public override void Read(DataContext ctx, BinaryReader reader) {
            Name = reader.ReadNullTerminatedString();
            FullName = reader.ReadNullTerminatedString();
            DisplayName = reader.ReadNullTerminatedString();
        }

        public override void Write(DataContext ctx, BinaryWriter writer) {
            writer.WriteNullTerminatedString(Name);
            writer.WriteNullTerminatedString(FullName);
            writer.WriteNullTerminatedString(DisplayName);
        }

        public override string ToString()
            => $"#{ID}: {FullName} ({Name})";

    }


    public abstract class MetaPlayerBaseType<T> : MetaType<T> where T : MetaPlayerBaseType<T> {

        public MetaPlayerBaseType() {
        }
        public MetaPlayerBaseType(DataPlayerInfo? player) {
            Opt = player;
        }

        public DataPlayerInfo? Opt;
        public DataPlayerInfo Player {
            get => Opt ?? throw new Exception($"{GetType().Name} with actual player expected.");
            set => Opt = value ?? throw new Exception($"{GetType().Name} with actual player expected.");
        }

        public override void Read(DataContext ctx, MetaTypeWrap data) {
            ctx.TryGetRef(uint.Parse(data["ID"]), out Opt);
        }

        public override void Write(DataContext ctx, MetaTypeWrap data) {
            data["ID"] = (Opt?.ID ?? uint.MaxValue).ToString();
        }

        public static implicit operator DataPlayerInfo(MetaPlayerBaseType<T> meta)
            => meta.Player;

    }


    public class MetaPlayerUpdate : MetaPlayerBaseType<MetaPlayerUpdate> {

        static MetaPlayerUpdate() {
            MetaID = "playerUpd";
        }

        public MetaPlayerUpdate()
            : base() {
        }
        public MetaPlayerUpdate(DataPlayerInfo? player)
            : base(player) {
        }

    }

    public class MetaPlayerPrivateState : MetaPlayerBaseType<MetaPlayerPrivateState> {

        static MetaPlayerPrivateState() {
            MetaID = "playerPrivSt";
        }

        public MetaPlayerPrivateState()
            : base() {
        }
        public MetaPlayerPrivateState(DataPlayerInfo? player)
            : base(player) {
        }

    }

    public class MetaPlayerPublicState : MetaPlayerBaseType<MetaPlayerPublicState> {

        static MetaPlayerPublicState() {
            MetaID = "playerPubSt";
        }

        public MetaPlayerPublicState()
            : base() {
        }
        public MetaPlayerPublicState(DataPlayerInfo? player)
            : base(player) {
        }

    }
}
