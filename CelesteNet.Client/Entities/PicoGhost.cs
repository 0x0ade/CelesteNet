

using System.Reflection;
using Celeste.Pico8;

namespace Celeste.Mod.CelesteNet.Client.Entities;

public class PicoGhost : Classic.ClassicObject {
    static readonly MethodInfo draw_player = 
        typeof(Classic)
        .GetMethod("draw_player", BindingFlags.Instance | BindingFlags.NonPublic);

    public int djump;

    public override void init(Classic g, Emulator e)
    {
        base.init(g, e);
        spr = 1f;
    }

    public override void draw()
    {
        draw_player.Invoke(G, new object[] { this, djump });
    }

    public override string ToString() {
        return $"{{x: {x}, y: {y}, flipx: {flipX}, flipy: {flipY}, spr: {spr}, djump: {djump}}}";
    }
}