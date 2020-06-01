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
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet {
    public delegate void DataHandler(CelesteNetConnection con, DataType data);
    public delegate void DataHandler<T>(CelesteNetConnection con, T data) where T : DataType<T>;
    public class DataContext {

        public readonly Dictionary<string, Type> IDToTypeMap = new Dictionary<string, Type>();
        public readonly Dictionary<Type, string> TypeToIDMap = new Dictionary<Type, string>();

        public readonly Dictionary<Type, DataHandler> Handlers = new Dictionary<Type, DataHandler>();

        public DataContext() {
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(DataType).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                string id = null;
                for (Type parent = type; parent != typeof(DataType) && string.IsNullOrEmpty(id); parent = parent.BaseType) {
                    id = parent.GetField("DataID", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                }

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
            }
        }

        public void RegisterHandler<T>(DataHandler<T> handler) where T : DataType<T>
            => RegisterHandler(typeof(T), (con, data) => handler(con, (T) data));

        public void RegisterHandler(Type type, DataHandler handler) {
            if (Handlers.TryGetValue(type, out DataHandler existing)) {
                handler = existing + handler;
            }
            Handlers[type] = handler;
        }

        public void RegisterHandlersIn(object handlers) {
            foreach (MethodInfo method in handlers.GetType().GetMethods()) {
                if (method.Name != "Handle")
                    continue;

                ParameterInfo[] args = method.GetParameters();
                if (args.Length != 2 || !args[0].ParameterType.IsCompatible(typeof(CelesteNetConnection)))
                    continue;

                Type argType = args[1].ParameterType;
                if (!argType.IsCompatible(typeof(DataType)))
                    continue;

                RegisterHandler(argType, (con, data) => method.Invoke(handlers, new object[] { con, data }));
            }
        }

        public DataType Read(BinaryReader reader) {
            string id = Calc.ReadNullTerminatedString(reader);
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

        public int Write(BinaryWriter writer, DataType data)
            => Write(writer, data.GetType(), data);

        public int Write<T>(BinaryWriter writer, T data) where T : DataType<T>
            => Write(writer, typeof(T), data);

        protected int Write(BinaryWriter writer, Type type, DataType data) {
            if (!TypeToIDMap.TryGetValue(type, out string id))
                throw new Exception($"Unknown data type {type} ({data})");

            long startAll = writer.BaseStream.Position;

            writer.WriteNullTerminatedString(id);
            writer.Write((ushort) data.DataFlags);
            writer.Write((ushort) 0); // Filled in later.
            writer.Flush();

            long startData = writer.BaseStream.Position;

            data.Write(writer);
            writer.Flush();

            long end = writer.BaseStream.Position;

            writer.BaseStream.Seek(startData - 2, SeekOrigin.Begin);
            long length = end - startData;
            if (length > ushort.MaxValue)
                length = ushort.MaxValue;
            writer.Write((ushort) length);
            writer.Flush();
            writer.BaseStream.Seek(end, SeekOrigin.Begin);

            return (int) (end - startAll);
        }

        public byte[] ToBytes(DataType data)
            => ToBytes(data.GetType(), data);

        public byte[] ToBytes<T>(T data) where T : DataType<T>
            => ToBytes(typeof(T), data);

        protected byte[] ToBytes(Type type, DataType data) {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8)) {
                Write(writer, type, data);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public void Handle(CelesteNetConnection con, DataType data)
            => Handle(con, data?.GetType(), data);

        public void Handle<T>(CelesteNetConnection con, T data) where T : DataType<T>
            => Handle(con, typeof(T), data);

        protected void Handle(CelesteNetConnection con, Type type, DataType data) {
            if (type == null || data == null)
                return;

            for (; type != typeof(DataType); type = type.BaseType) {
                if (Handlers.TryGetValue(type, out DataHandler handler)) {
                    handler(con, data);
                }
            }
        }

    }
}
