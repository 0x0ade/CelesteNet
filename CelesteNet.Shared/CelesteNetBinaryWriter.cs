using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectFactories;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetBinaryWriter : BinaryWriter {

        public readonly DataContext Data;

        public OptMap<string>? Strings;
        public OptMap<Type>? CoreTypeMap;

        protected ConcurrentStack<Tuple<long, int>> SizeDummyStack = new();

        public CelesteNetBinaryWriter(DataContext ctx, OptMap<string>? strings, OptMap<Type>? coreTypeMap, Stream output)
            : base(output, CelesteNetUtils.UTF8NoBOM) {
            Data = ctx;
            Strings = strings;
            CoreTypeMap = coreTypeMap;
        }

        public CelesteNetBinaryWriter(DataContext ctx, OptMap<string>? strings, OptMap<Type>? coreTypeMap, Stream output, bool leaveOpen)
            : base(output, CelesteNetUtils.UTF8NoBOM, leaveOpen) {
            Data = ctx;
            Strings = strings;
            CoreTypeMap = coreTypeMap;
        }

        public virtual void WriteSizeDummy(int size) {
            Flush();
            long pos = BaseStream.Position;

            if (size == 1)
                Write((byte) 0);
            else if (size == 2)
                Write((ushort) 0);
            else if (size == 4)
                Write((uint) 0);
            else
                throw new ArgumentException($"Invalid size dummy size {size}");

            SizeDummyStack.Push(new(pos, size));
        }

        public virtual void UpdateSizeDummy() {
            if (!SizeDummyStack.TryPop(out Tuple<long, int>? dummy))
                throw new InvalidOperationException("No size dummy on the stack");
            long dummyPos = dummy.Item1;
            int dummySize = dummy.Item2;

            Flush();
            long end = BaseStream.Position;
            long length = end - (dummyPos + dummySize);

            BaseStream.Seek(dummyPos, SeekOrigin.Begin);

            if (dummySize == 1)
                Write((byte) length);
            else if (dummySize == 2)
                Write((ushort) length);
            else if (dummySize == 4)
                Write((uint) length);

            Flush();
            BaseStream.Seek(end, SeekOrigin.Begin);
        }

        public new void Write7BitEncodedInt(int value)
            => base.Write7BitEncodedInt(value);
        public void Write7BitEncodedUInt(uint value)
            => Write7BitEncodedInt(unchecked((int) value));

        public virtual void Write(Vector2 value) {
            Write(value.X);
            Write(value.Y);
        }

        public virtual void Write(Color value) {
            Write(value.R);
            Write(value.G);
            Write(value.B);
            Write(value.A);
        }

        public virtual void WriteNoA(Color value) {
            Write(value.R);
            Write(value.G);
            Write(value.B);
        }

        public virtual void Write(DateTime value) {
            Write(value.ToBinary());
        }

        public virtual unsafe void WriteNetString(string? text) {
            if (text != null && text.Length != 0) {
                if (NetStringCtrl.First <= text[0] && text[0] <= NetStringCtrl.Last) {
                    if (text.Length > 1)
                        throw new ArgumentException("Control strings cannot be used to start a string.");
                    Write((byte) text[0]);
                    return;
                }

                if (text.Length > 4096)
                    throw new ArgumentException("String too long.");
                fixed (char* src = text) {
                    char* ptr = src;
                    for (int i = text.Length; i > 0; i--) {
                        if (*ptr == '\0')
                            throw new ArgumentException("String contains null byte.");
                        Write(*ptr++);
                    }
                }
            }
            Write('\0');
        }

        public virtual void WriteNetMappedString(string? text) {
            if (Strings == null || !Strings.TryMap(text, out int id)) {
                WriteNetString(text);
                return;
            }

            Write((byte) NetStringCtrl.Mapped);
            Write7BitEncodedInt(id);
        }

        public virtual bool TryGetSlimID(Type type, out int slimID) {
            slimID = -1;
            return CoreTypeMap != null && CoreTypeMap.TryMap(type, out slimID);
        }

        public void WriteRef<T>(T? data) where T : DataType<T>
            => Write7BitEncodedUInt(data?.Get<MetaRef>(Data) ?? throw new ArgumentException($"Expected {Data.DataTypeToID[typeof(T)]} MetaRef to write, got null"));

        public void WriteOptRef<T>(T? data) where T : DataType<T>
            => Write7BitEncodedUInt(data?.GetOpt<MetaRef>(Data) ?? uint.MaxValue);

    }
}
