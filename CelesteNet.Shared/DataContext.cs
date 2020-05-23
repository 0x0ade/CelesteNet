using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Monocle;

namespace Celeste.Mod.CelesteNet {
    public delegate void DataHandler(DataType data);
    public delegate void DataHandler<T>(T data) where T : DataType<T>;
    public class DataContext {

        public readonly Dictionary<string, Type> IDToTypeMap = new Dictionary<string, Type>();
        public readonly Dictionary<Type, string> TypeToIDMap = new Dictionary<Type, string>();
        public readonly Dictionary<string, DataFlags> Flags = new Dictionary<string, DataFlags>();

        public readonly Dictionary<string, DataHandler> Handlers = new Dictionary<string, DataHandler>();

        public DataContext() {
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(DataType).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                string id = (
                    type.GetField(nameof(DataType.DataID), BindingFlags.Public | BindingFlags.Static) ??
                    typeof(DataType).GetField(nameof(DataType.DataID), BindingFlags.Public | BindingFlags.Static)
                ).GetValue(null) as string;

                if (string.IsNullOrEmpty(id)) {
                    Logger.Log(LogLevel.WRN, "data", $"Found data type {type.FullName} but no DataID");
                    continue;
                }

                if (IDToTypeMap.ContainsKey(id)) {
                    Logger.Log(LogLevel.WRN, "data", $"Found data type {type.FullName} but conflicting ID {id}");
                    continue;
                }

                Logger.Log(LogLevel.INF, "data", $"Found data type {type.FullName} with ID {id}");
                IDToTypeMap[id] = type;
                TypeToIDMap[type] = id;
                Flags[id] = (
                    type.GetField(nameof(DataType.DataFlags), BindingFlags.Public | BindingFlags.Static) ??
                    typeof(DataType).GetField(nameof(DataType.DataFlags), BindingFlags.Public | BindingFlags.Static)
                ).GetValue(null) as DataFlags? ?? DataFlags.None;
            }
        }

        public void RegisterHandler<T>(DataHandler<T> handler) where T : DataType<T> {
            DataHandler wrapped = data => handler((T) data);
            string id = TypeToIDMap[typeof(T)];
            if (Handlers.TryGetValue(id, out DataHandler existing)) {
                wrapped = existing + wrapped;
            }
            Handlers[id] = wrapped;
        }

        public DataType Read(BinaryReader reader) {
            string id = reader.ReadNullTerminatedString();
            DataFlags flags = (DataFlags) reader.ReadUInt16();
            ushort length = reader.ReadUInt16();

            if (!IDToTypeMap.TryGetValue(id, out Type type)) {
                return new DataUnparsed() {
                    InnerID = id,
                    InnerFlags = flags,
                    InnerData = reader.ReadBytes(length)
                };
            }

            DataType data = (DataType) Activator.CreateInstance(type);
            data.Read(reader);
            return data;
        }

        public long Write(BinaryWriter writer, DataType data)
            => Write(writer, data, data.GetType());

        public long Write<T>(BinaryWriter writer, T data) where T : DataType<T>
            => Write(writer, data, typeof(T));

        public long Write(BinaryWriter writer, DataType data, Type type) {
            string id = TypeToIDMap[type];
            writer.WriteNullTerminatedString(id);
            writer.Write((ushort) Flags[id]);
            writer.Write((ushort) 0); // Filled in later.
            writer.Flush();

            long start = writer.BaseStream.Position;

            data.Write(writer);
            writer.Flush();

            long end = writer.BaseStream.Position;

            writer.BaseStream.Seek(start, SeekOrigin.Begin);
            long length = end - start;
            if (length > ushort.MaxValue)
                length = ushort.MaxValue;
            writer.Write((ushort) length);
            writer.Flush();
            writer.BaseStream.Seek(end, SeekOrigin.Begin);

            return length;
        }

    }
}
