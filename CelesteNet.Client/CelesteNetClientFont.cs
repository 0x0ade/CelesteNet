using MC = Mono.Cecil;
using CIL = Mono.Cecil.Cil;

using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client {
    // Copy of ActiveFont that always uses the English font.
    public static class CelesteNetClientFont {

        public static PixelFont Font => Fonts.Get(Dialog.Languages["english"].FontFace);

        public static PixelFontSize FontSize => Font.Get(BaseSize);

        public static float BaseSize => Dialog.Languages["english"].FontFaceSize;

        public static float LineHeight => Font.Get(BaseSize).LineHeight;

        public static Vector2 Measure(char text) 
            => Font.Get(BaseSize).Measure(text);

        public static Vector2 Measure(string text) 
            => Font.Get(BaseSize).Measure(text);

        public static float WidthToNextLine(string text, int start) 
            => Font.Get(BaseSize).WidthToNextLine(text, start);

        public static float HeightOf(string text) 
            => Font.Get(BaseSize).HeightOf(text);

        public static void Draw(char character, Vector2 position, Vector2 justify, Vector2 scale, Color color) 
            => Font.Draw(BaseSize, character, position, justify, scale, color);

        private static void Draw(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color, float edgeDepth, Color edgeColor, float stroke, Color strokeColor) 
            => Font.Draw(BaseSize, text, position, justify, scale, color, edgeDepth, edgeColor, stroke, strokeColor);

        public static void Draw(string text, Vector2 position, Color color) 
            => Draw(text, position, Vector2.Zero, Vector2.One, color, 0f, Color.Transparent, 0f, Color.Transparent);

        public static void Draw(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color) 
            => Draw(text, position, justify, scale, color, 0f, Color.Transparent, 0f, Color.Transparent);

        public static void DrawOutline(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color, float stroke, Color strokeColor) 
            => Draw(text, position, justify, scale, color, 0f, Color.Transparent, stroke, strokeColor);

        public static void DrawEdgeOutline(string text, Vector2 position, Vector2 justify, Vector2 scale, Color color, float edgeDepth, Color edgeColor, float stroke = 0f, Color strokeColor = default(Color)) 
            => Draw(text, position, justify, scale, color, edgeDepth, edgeColor, stroke, strokeColor);

    }
}
