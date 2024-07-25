using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Pico8;
using Microsoft.Xna.Framework;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Client.Entities;

public class PicoGhost : Classic.ClassicObject {
    DynamicData GData;
    public int djump;
    internal DataPicoState.HairNode[] hair = new DataPicoState.HairNode[5];
    internal uint id;

    public override void init(Classic g, Emulator e)
    {
        base.init(g, e);
        GData = new(g);
        spr = 1f;
    }

    public override void draw()
    {
        int num = djump switch {
			2 => 7 + E.flr((int) GData.Get("frames") / 3 % 2) * 4, 
			1 => 8, 
			_ => 12, 
		};

        int facing =  (!flipX) ? 1 : (-1);
		Vector2 vector = new(x + 4f - (facing * 2), y + (E.btn((int) GData.Get("k_down")) ? 4 : 3));
		foreach (var node in hair) {
			var x = node.X + (vector.X - node.X) / 1.5f;
			var y = node.Y + (vector.Y + 0.5f - node.Y) / 1.5f;
			E.circfill(node.X, node.Y, node.Size, num);
			vector = new Vector2(node.X, node.Y);
		}
        GData.Invoke("draw_player", new object[] { this, djump });
        E.print(id.ToString(), x, y + 1, 7f);

    }

    public override string ToString() {
        return $"{{x: {x}, y: {y}, flipx: {flipX}, flipy: {flipY}, spr: {spr}, djump: {djump}}}";
    }
}