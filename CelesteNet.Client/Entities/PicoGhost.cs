using System.Collections.Generic;
using System.Linq;
using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Pico8;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Client.Entities;

public class PicoGhost : Classic.ClassicObject {
    private DynamicData _gData;
    public int DJump;
    internal List<DataPicoFrame.HairNode> Hair = new();
    internal CelesteNetPico8Component Pico8Component;
    public readonly DataPlayerInfo Player;
    string Name {
        get {
            // The reason why this is done is that
            // if you have two players with the same name,
            // say Player and Player#2,
            // from Player#2's perspective, both people's
            // Player.FullName's are Player, dropping the #N.
            // Also, the PICO-8 font doesn't support emotes.
            var name = Player.DisplayName ?? "???";
            var colonIndex = name.LastIndexOf(':');
            if (colonIndex > -1) {
                name = name[(colonIndex + 2)..];
            }
            return name;
        }
    }

    public PicoGhost(DataPlayerInfo player, CelesteNetPico8Component pico8Component) {
        Player = player;
        Pico8Component = pico8Component;
    }

    public override void init(Classic g, Emulator e) {
        base.init(g, e);

        _gData = new(g);
        spr = 1f;
    }

    public override void draw() {

        int num = DJump switch {
			2 => 7 + E.flr((int) _gData.Get("frames") / 3 % 2) * 4,
            1 => 8,
            _ => 12,
        };


        if (CelesteNetClientModule.Settings.InGame.OtherPlayerOpacity > 0)
        {
            foreach (var node in Hair.Where(node => node != null))
                E.circfill(node.X, node.Y, node.Size, num);
            _gData.Invoke("draw_player", new object[] { this, DJump });
        }
        
        if (CelesteNetClientModule.Settings.InGameHUD.NameOpacity > 0) {
            Pico8FontHelper.PrintOutlinedCenter(Name, (int) x + 4, (int) y - 8);
        }
    }

    public override string ToString() {
        return $"PicoGhost(id: {Player.ID}, x: {x}, y: {y}, flipx: {flipX}, flipy: {flipY}, spr: {spr}, djump: {DJump})";
    }
}