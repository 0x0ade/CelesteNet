using System.Linq;

namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataPicoState : DataType<DataPicoState> {
        public DataPlayerInfo? Player;
        public float Spr;
        // These will probably be accessed before they're initialized, let's be honest here.
        // We set them to NaN so that we don't explode from a null deref.
        public float X = float.NaN;
        public float Y = float.NaN;
        public int Type;
        public bool FlipY;
        public bool FlipX;
        public int Djump;
        public bool Dead = false;
        public bool PlayKillAnimation = false;

        public override DataFlags DataFlags => DataFlags.Unreliable |  DataFlags.NoStandardMeta;

        public HairNode[] Hair = new HairNode[5] { new(), new(), new(), new(), new() };
        public int Level;

        public class HairNode {
            public float X = float.NaN;
            public float Y = float.NaN;
            public float Size = 0;

            public override string ToString()
            {
                return $"HairNode(x: {X}, y: {Y}, size: {Size})";
            }
        }

        public override MetaType[] GenerateMeta(DataContext ctx)
            => new MetaType[] {
                new MetaPlayerUpdate(Player),
                new MetaOrderedUpdate(Player?.ID ?? uint.MaxValue)
            };

        public override void FixupMeta(DataContext ctx) {
            MetaPlayerUpdate playerUpd = Get<MetaPlayerUpdate>(ctx);
            MetaOrderedUpdate order = Get<MetaOrderedUpdate>(ctx);

            order.ID = playerUpd;
            Player = playerUpd;
        }

        static DataPicoState() {
            DataID = "picostate";
        }

        public override string ToString() {
            return $"DataPicoState(player: {Player}, x: {X}, y: {Y}, spr: {Spr}, type: {Type}, flipX: {FlipX}, flipY: {FlipY}, djump: {Djump}, level: {Level}, dead: {Dead}, hair: [{string.Join(", ", Hair.Select(node => node.ToString()))}])";
        }

        protected override void Read(CelesteNetBinaryReader reader) {
            Player = reader.ReadOptRef<DataPlayerInfo>();
            Spr = reader.ReadSingle();
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
            FlipX = reader.ReadBoolean();
            FlipY = reader.ReadBoolean();
            Djump = reader.ReadInt32();
            Type = reader.ReadInt32();
            Level = reader.ReadInt32();
            Dead = reader.ReadBoolean();
            for (int i = 0; i < 5; i++) {
                HairNode node = Hair[i];
                node.X = reader.ReadSingle();
                node.Y = reader.ReadSingle();
                node.Size = reader.ReadSingle();
            }
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.WriteOptRef(Player);
            writer.Write(Spr);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(FlipX);
            writer.Write(FlipY);
            writer.Write(Djump);
            writer.Write(Type);
            writer.Write(Level);
            writer.Write(Dead);
            for (int i = 0; i < 5; i++) {
                HairNode node = Hair[i];
                writer.Write(node.X);
                writer.Write(node.Y);
                writer.Write(node.Size);
            }
        }
    }
}