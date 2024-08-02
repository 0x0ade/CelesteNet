using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client;

public static class Pico8FontHelper {
    private static readonly MTexture Font = GFX.Gui["celestenet/pico8/bytesized_font"];

    private static readonly Color PicoBlack = new(0, 0, 0);
    private static readonly Color PicoWhite = new(0xff, 0xf1, 0xe8);

    private static MTexture CharacterSprite(uint codepoint) {
        if (codepoint is < 32 or > 126) {
            codepoint = 63; // Codepoint of question mark
        }
        var index = codepoint - 32;
        int x = (int) index % 16 * 3;
        int y = (int) index / 16 * 4;
        return new MTexture(Font, x, y, 3, 4);
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