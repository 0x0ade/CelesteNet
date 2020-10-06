using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectFactories;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetBinaryReader : BinaryReader {

        public readonly DataContext Data;

        public StringMap? StringMap;

        public CelesteNetBinaryReader(DataContext ctx, Stream input)
            : base(input) {
            Data = ctx;
        }

        public CelesteNetBinaryReader(DataContext ctx, Stream input, Encoding encoding)
            : base(input, encoding) {
            Data = ctx;
        }

        public CelesteNetBinaryReader(DataContext ctx, Stream input, Encoding encoding, bool leaveOpen)
            : base(input, encoding, leaveOpen) {
            Data = ctx;
        }

        public virtual Vector2 ReadVector2()
            => new Vector2(ReadSingle(), ReadSingle());
        public virtual Vector2 ReadVector2Scale()
            => new Vector2(Calc.Clamp(ReadSingle(), -3f, 3f), Calc.Clamp(ReadSingle(), -3f, 3f));

        public virtual Color ReadColor()
            => new Color(ReadByte(), ReadByte(), ReadByte(), ReadByte());

        public virtual Color ReadColorNoA()
            => new Color(ReadByte(), ReadByte(), ReadByte(), 255);

        public virtual DateTime ReadDateTime()
            => DateTime.FromBinary(ReadInt64());

        public virtual string ReadNetString() {
            StringBuilder sb = new StringBuilder();
            char c;
            while ((c = ReadChar()) != '\0') {
                sb.Append(c);
                if (sb.Length > 4096)
                    throw new Exception("String too long.");
            }
            return sb.ToString();
        }

        public virtual string ReadNetMappedString() {
            byte b = ReadByte();
            if (b == 0xFF) {
                if (StringMap == null)
                    throw new Exception("Trying to read a mapped string without a string map!");
                string value = StringMap.Get(ReadUInt16());
                if (ReadChar() != '\0')
                    throw new Exception("Malformed mapped string.");
                return value;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append((char) b);
            char c;
            while ((c = ReadChar()) != '\0') {
                sb.Append(c);
                if (sb.Length > 4096)
                    throw new Exception("String too long.");
            }
            return sb.ToString();
        }

        public T? ReadRef<T>() where T : DataType<T>
            => Data.GetRef<T>(ReadUInt32());

        public T? ReadOptRef<T>() where T : DataType<T>
            => Data.TryGetRef(ReadUInt32(), out T? value) ? value : null;

    }
}
