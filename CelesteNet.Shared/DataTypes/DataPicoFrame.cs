using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.CelesteNet.DataTypes;

public class DataPicoFrame : DataType<DataPicoFrame> {
    public DataPlayerInfo? Player;
    public float Spr;
    // These will probably be accessed before they're initialized, let's be honest here.
    // We set them to NaN so that we don't default to drawing in the top right corner (0, 0).
    public float X = float.NaN;
    public float Y = float.NaN;
    public int Type;
    public bool FlipY;
    public bool FlipX;
    public int DJump;
    public bool Dead;

    public override DataFlags DataFlags => DataFlags.Unreliable |  DataFlags.NoStandardMeta;

    public List<HairNode> Hair = new();
    public int Level;

    public class HairNode {
        public float X = float.NaN;
        public float Y = float.NaN;
        public float Size = float.NaN;

        public override string ToString() => $"HairNode(x: {X}, y: {Y}, size: {Size})";
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

    static DataPicoFrame() {
        DataID = "picoframe";
    }

    public override string ToString()
        => $"DataPicoFrame(player: {Player}, x: {X}, y: {Y}, spr: {Spr}, type: {Type}, flipX: {FlipX}, flipY: {FlipY}, djump: {DJump}, level: {Level}, dead: {Dead}, hair: [{string.Join(", ", Hair.Select(node => node.ToString()))}])";

    protected override void Read(CelesteNetBinaryReader reader) {
        Player = reader.ReadOptRef<DataPlayerInfo>();
        Spr = reader.ReadSingle();
        X = reader.ReadSingle();
        Y = reader.ReadSingle();
        FlipX = reader.ReadBoolean();
        FlipY = reader.ReadBoolean();
        DJump = reader.ReadInt32();
        Type = reader.ReadInt32();
        Level = reader.ReadInt32();
        Dead = reader.ReadBoolean();
        ushort length = reader.ReadUInt16();
        Hair.Clear();
        for (int i = 0; i < length; i++) {
            HairNode node = new() {
                X = reader.ReadSingle(),
                Y = reader.ReadSingle(),
                Size = reader.ReadSingle()
            };
            Hair.Add(node);
        }
    }

    protected override void Write(CelesteNetBinaryWriter writer) {
        writer.WriteOptRef(Player);
        writer.Write(Spr);
        writer.Write(X);
        writer.Write(Y);
        writer.Write(FlipX);
        writer.Write(FlipY);
        writer.Write(DJump);
        writer.Write(Type);
        writer.Write(Level);
        writer.Write(Dead);
        writer.Write((ushort) Hair.Count);
        foreach (HairNode node in Hair) {
            writer.Write(node.X);
            writer.Write(node.Y);
            writer.Write(node.Size);
        }
    }
}