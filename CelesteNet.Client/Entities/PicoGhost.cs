using System;
using System.Collections.Generic;
using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Pico8;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Client.Entities;

public class FontHelper {
    static readonly MTexture font = GFX.Gui["celestenet/pico8/bytesized_font"];

    public static readonly Color PicoBlack = new(0, 0, 0);
    public static readonly Color PicoWhite = new(0xff, 0xf1, 0xe8);

    internal static MTexture CharacterSprite(uint codepoint) {
        if (codepoint < 32 || codepoint > 126) {
            codepoint = 63; // Codepoint of question mark
        }
        var index = codepoint - 32;
        int x = (int) index % 16 * 3;
        int y = (int) index / 16 * 4;
        return new MTexture(font, x, y, 3, 4);
    }

    public static void Print(string text, int x, int y) {
        Print(text, x, y, PicoWhite);
    }

    public static void Print(string text, int x, int y, Color color) {
        int initialX = x;
        for (int i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1) {
            var codepoint = char.ConvertToUtf32(text, i);
            if (codepoint == 0x0A) {
                // Newline
                x = initialX;
                y += 5;
                continue;
            }
            var sprite = CharacterSprite((uint) codepoint);

            sprite.Draw(new Vector2(x, y), Vector2.Zero, color);
            x += 4;
        }
    }
    public static void PrintOutlinedCenter(string text, int x, int y) {
        PrintOutlinedCenter(text, x, y, PicoWhite, PicoBlack);
    }

    public static void PrintOutlinedCenter(string text, int x, int y, Color inside, Color outside) {
        Print(text, x - (text.Length / 2 * 4) - 1, y - 1, outside);
        Print(text, x - (text.Length / 2 * 4)    , y - 1, outside);
        Print(text, x - (text.Length / 2 * 4) + 1, y - 1, outside);
        Print(text, x - (text.Length / 2 * 4) - 1, y    , outside);
        Print(text, x - (text.Length / 2 * 4) + 1, y    , outside);
        Print(text, x - (text.Length / 2 * 4) - 1, y + 1, outside);
        Print(text, x - (text.Length / 2 * 4)    , y + 1, outside);
        Print(text, x - (text.Length / 2 * 4) + 1, y + 1, outside);
        Print(text, x - (text.Length / 2 * 4)    , y    , inside);
    }
}

public class PicoGhost : Classic.ClassicObject {
    DynamicData GData;
    public int djump;
    internal List<DataPicoState.HairNode> hair = new();
    internal CelesteNetPico8Component Pico8Component;
    public DataPlayerInfo Player;
    public string Name {
        get {
            // The reason why this is done is because
            // if you have two players with the same name,
            // say Player and Player#2,
            // from Player#2's perspective, both people's
            // Player.FullName's are Player, dropping the #N.
            // Also the PICO-8 font doesn't support emotes.
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

        GData = new(g);
        spr = 1f;
    }

    public override void draw() {

        int num = djump switch {
			2 => 7 + E.flr((int) GData.Get("frames") / 3 % 2) * 4, 
			1 => 8, 
			_ => 12, 
		};

        int facing =  (!flipX) ? 1 : (-1);
		Vector2 vector = new(x + 4f - (facing * 2), y + (E.btn((int) GData.Get("k_down")) ? 4 : 3));
		foreach (var node in hair) {
            if (node == null) { continue; }
			var x = node.X + (vector.X - node.X) / 1.5f;
			var y = node.Y + (vector.Y + 0.5f - node.Y) / 1.5f;
			E.circfill(node.X, node.Y, node.Size, num);
			vector = new Vector2(node.X, node.Y);
		}

        if (CelesteNetClientModule.Settings.InGame.OtherPlayerOpacity > 0) {
            GData.Invoke("draw_player", new object[] { this, djump });
        }
        
        if (CelesteNetClientModule.Settings.InGameHUD.NameOpacity > 0) {
            FontHelper.PrintOutlinedCenter(Name, (int) x + 4, (int) y - 8);
        }
    }

    public override string ToString() {
        return $"PicoGhost(id: {Player.ID}, x: {x}, y: {y}, flipx: {flipX}, flipy: {flipY}, spr: {spr}, djump: {djump})";
    }
}