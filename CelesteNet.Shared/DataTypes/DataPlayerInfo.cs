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

        public uint ID;
        public string Name = "";
        public string FullName = "";
        public string DisplayName = "";

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaRef(ID, !FullName.IsNullOrEmpty())
            };

        public override void FixupMeta(DataContext ctx) {
            ID = Get<MetaRef>(ctx);
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            Name = reader.ReadNetString();
            FullName = reader.ReadNetString();
            DisplayName = reader.ReadNetString();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteNetString(Name);
            writer.WriteNetString(FullName);
            writer.WriteNetString(DisplayName);
        }

        public override string ToString()
            => $"#{ID}: {FullName} ({Name})";

    }


    public abstract class MetaPlayerBaseType<T> : MetaType<T> where T : MetaPlayerBaseType<T> {

        public MetaPlayerBaseType() {
        }
        public MetaPlayerBaseType(DataPlayerInfo? player) {
            Player = player;
        }

        public DataPlayerInfo? Player;
        public DataPlayerInfo ForcePlayer {
            get => Player ?? throw new Exception($"{GetType().Name} with actual player expected.");
            set => Player = value ?? throw new Exception($"{GetType().Name} with actual player expected.");
        }

        public override void Read(CelesteNetBinaryReader reader) {
            reader.Data.TryGetRef(reader.Read7BitEncodedUInt(), out Player);
        }

        public override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedUInt(Player?.ID ?? uint.MaxValue);
        }

        public static implicit operator DataPlayerInfo?(MetaPlayerBaseType<T> meta)
            => meta.Player;

        public static implicit operator uint(MetaPlayerBaseType<T> meta)
            => meta.Player?.ID ?? uint.MaxValue;

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
