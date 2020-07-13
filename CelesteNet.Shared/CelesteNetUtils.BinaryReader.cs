using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
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

        public static Vector2 ReadVector2(this BinaryReader reader)
            => new Vector2(reader.ReadSingle(), reader.ReadSingle());
        public static void Write(this BinaryWriter writer, Vector2 value) {
            writer.Write(value.X);
            writer.Write(value.Y);
        }

        public static Color ReadColor(this BinaryReader reader)
            => new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
        public static void Write(this BinaryWriter writer, Color value) {
            writer.Write(value.R);
            writer.Write(value.G);
            writer.Write(value.B);
            writer.Write(value.A);
        }

        public static Color ReadColorNoA(this BinaryReader reader)
            => new Color(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);
        public static void WriteNoA(this BinaryWriter writer, Color value) {
            writer.Write(value.R);
            writer.Write(value.G);
            writer.Write(value.B);
        }

        public static DateTime ReadDateTime(this BinaryReader reader)
            => DateTime.FromBinary(reader.ReadInt64());
        public static void Write(this BinaryWriter writer, DateTime value) {
            writer.Write(value.ToBinary());
        }

        public static string ReadNetString(this BinaryReader stream) {
            StringBuilder sb = new StringBuilder();
            char c;
            while ((c = stream.ReadChar()) != '\0') {
                sb.Append(c);
                if (sb.Length > 4096)
                    throw new Exception("String too long.");
            }
            return sb.ToString();
        }

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
