using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public static partial class CelesteNetUtils {

        // Needed for backwards compatibility with older mods which don't support CelesteNetBinaryRW.

        [Obsolete("Use CelesteNetBinaryReader instead.")]
        public static Vector2 ReadVector2(this BinaryReader reader)
            => new(reader.ReadSingle(), reader.ReadSingle());
        [Obsolete("Use CelesteNetBinaryReader instead.")]
        public static Vector2 ReadVector2Scale(this BinaryReader reader)
            => new(Calc.Clamp(reader.ReadSingle(), -3f, 3f), Calc.Clamp(reader.ReadSingle(), -3f, 3f));
        [Obsolete("Use CelesteNetBinaryWriter instead.")]
        public static void Write(this BinaryWriter writer, Vector2 value) {
            writer.Write(value.X);
            writer.Write(value.Y);
        }

        [Obsolete("Use CelesteNetBinaryReader instead.")]
        public static Color ReadColor(this BinaryReader reader)
            => new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        [Obsolete("Use CelesteNetBinaryWriter instead.")]
        public static void Write(this BinaryWriter writer, Color value) {
            writer.Write(value.R);
            writer.Write(value.G);
            writer.Write(value.B);
            writer.Write(value.A);
        }

        [Obsolete("Use CelesteNetBinaryReader instead.")]
        public static Color ReadColorNoA(this BinaryReader reader)
            => new(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);
        [Obsolete("Use CelesteNetBinaryWriter instead.")]
        public static void WriteNoA(this BinaryWriter writer, Color value) {
            writer.Write(value.R);
            writer.Write(value.G);
            writer.Write(value.B);
        }

        [Obsolete("Use CelesteNetBinaryReader instead.")]
        public static DateTime ReadDateTime(this BinaryReader reader)
            => DateTime.FromBinary(reader.ReadInt64());
        [Obsolete("Use CelesteNetBinaryWriter instead.")]
        public static void Write(this BinaryWriter writer, DateTime value) {
            writer.Write(value.ToBinary());
        }

        [Obsolete("Use CelesteNetBinaryReader instead.")]
        public static string ReadNetString(this BinaryReader stream) {
            StringBuilder sb = new();
            char c;
            while ((c = stream.ReadChar()) != '\0') {
                sb.Append(c);
                if (sb.Length > 4096)
                    throw new Exception("String too long.");
            }
            return sb.ToString();
        }

        [Obsolete("Use CelesteNetBinaryWriter instead.")]
        public static void WriteNetString(this BinaryWriter stream, string? text) {
            if (text != null) {
                if (text.Length > 4096)
                    throw new Exception("String too long.");
                for (int i = 0; i < text.Length; i++) {
                    char c = text[i];
                    stream.Write(c);
                }
            }
            stream.Write('\0');
        }

    }
}
