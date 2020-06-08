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
    public delegate bool DataFilter(CelesteNetConnection con, DataType data);
    public delegate bool DataFilter<T>(CelesteNetConnection con, T data) where T : DataType<T>;
    public class DataContext {

        public readonly Dictionary<string, Type> IDToTypeMap = new Dictionary<string, Type>();
        public readonly Dictionary<Type, string> TypeToIDMap = new Dictionary<Type, string>();

        public readonly Dictionary<Type, DataHandler> Handlers = new Dictionary<Type, DataHandler>();
        public readonly Dictionary<Type, DataFilter> Filters = new Dictionary<Type, DataFilter>();

        protected readonly Dictionary<Type, Dictionary<uint, IDataRefType>> References = new Dictionary<Type, Dictionary<uint, IDataRefType>>();
        protected readonly Dictionary<Type, Dictionary<uint, Dictionary<Type, IDataBoundRefType>>> Bound = new Dictionary<Type, Dictionary<uint, Dictionary<Type, IDataBoundRefType>>>();

        public DataContext() {
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(DataType).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                string id = null;
                for (Type parent = type; parent != typeof(DataType).BaseType && string.IsNullOrEmpty(id); parent = parent.BaseType) {
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

        public void RegisterFilter<T>(DataFilter<T> filter) where T : DataType<T>
            => RegisterFilter(typeof(T), (con, data) => filter(con, (T) data));

        public void RegisterFilter(Type type, DataFilter filter) {
            if (Filters.TryGetValue(type, out DataFilter existing)) {
                filter = existing + filter;
            }
            Filters[type] = filter;
        }

        public void RegisterHandlersIn(object handlers) {
            foreach (MethodInfo method in handlers.GetType().GetMethods()) {
                if (method.Name == "Handle" || method.Name == "Filter") {
                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length != 2 || !args[0].ParameterType.IsCompatible(typeof(CelesteNetConnection)))
                        continue;

                    Type argType = args[1].ParameterType;
                    if (!argType.IsCompatible(typeof(DataType)))
                        continue;

                    if (method.Name == "Filter") {
                        Logger.Log(LogLevel.VVV, "data", $"Autoregistering filter for {argType}: {method.GetID()}");
                        RegisterFilter(argType, (con, data) => (bool) method.Invoke(handlers, new object[] { con, data }));
                    } else {
                        Logger.Log(LogLevel.VVV, "data", $"Autoregistering handler for {argType}: {method.GetID()}");
                        RegisterHandler(argType, (con, data) => method.Invoke(handlers, new object[] { con, data }));
                    }
                }
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
            data.Read(this, reader);
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

            data.Write(this, writer);
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

            for (Type btype = type; btype != typeof(DataType).BaseType; btype = btype.BaseType)
                if (Filters.TryGetValue(btype, out DataFilter filter))
                    if (!filter.InvokeWhileTrue(con, data))
                        return;

            if (!data.FilterHandle(this))
                return;

            if (data is IDataRefType dataRef)
                SetRef(dataRef);

            for (Type btype = type; btype != typeof(DataType).BaseType; btype = btype.BaseType)
                if (Handlers.TryGetValue(btype, out DataHandler handler))
                    handler(con, data);
        }


        public T ReadRef<T>(BinaryReader reader) where T : DataType<T>, IDataRefType
            => GetRef<T>(reader.ReadUInt32());

        public T ReadOptRef<T>(BinaryReader reader) where T : DataType<T>, IDataRefType
            => TryGetRef(reader.ReadUInt32(), out T value) ? value : null;

        public void WriteRef<T>(BinaryWriter writer, T data) where T : DataType<T>, IDataRefType
            => writer.Write(data?.ID ?? uint.MaxValue);

        public T GetRef<T>(uint id) where T : DataType<T>, IDataRefType
            => (T) GetRef(typeof(T), id);

        public IDataRefType GetRef(Type type, uint id)
            => TryGetRef(type, id, out IDataRefType value) ? value : throw new Exception($"Unknown reference {type.FullName} ID {id}");

        public bool TryGetRef<T>(uint id, out T value) where T : DataType<T>, IDataRefType {
            bool rv = TryGetRef(typeof(T), id, out IDataRefType value_);
            value = (T) value_;
            return rv;
        }

        public bool TryGetRef(Type type, uint id, out IDataRefType value) {
            if (id == uint.MaxValue) {
                value = null;
                return true;
            }

            if (References.TryGetValue(type, out Dictionary<uint, IDataRefType> refs) &&
                refs.TryGetValue(id, out value)) {
                return true;
            }

            value = null;
            return false;
        }

        public T GetBoundRef<TBoundTo, T>(uint id) where TBoundTo : DataType<TBoundTo>, IDataRefType where T : DataType<T>, IDataBoundRefType<TBoundTo>
            => (T) GetBoundRef(typeof(TBoundTo), typeof(T), id);

        public T GetBoundRef<TBoundTo, T>(TBoundTo boundTo) where TBoundTo : DataType<TBoundTo>, IDataRefType where T : DataType<T>, IDataBoundRefType<TBoundTo>
            => (T) GetBoundRef(typeof(TBoundTo), typeof(T), boundTo.ID);

        public IDataBoundRefType GetBoundRef(Type typeBoundTo, Type type, uint id)
            => TryGetBoundRef(typeBoundTo, type, id, out IDataBoundRefType value) ? value : throw new Exception($"Unknown reference {typeBoundTo.FullName} bound to {type.FullName} ID {id}");

        public bool TryGetBoundRef<TBoundTo, T>(TBoundTo boundTo, out T value) where TBoundTo : DataType<TBoundTo>, IDataRefType where T : DataType<T>, IDataBoundRefType<TBoundTo>
            => TryGetBoundRef<TBoundTo, T>(boundTo.ID, out value);

        public bool TryGetBoundRef<TBoundTo, T>(uint id, out T value) where TBoundTo : DataType<TBoundTo>, IDataRefType where T : DataType<T>, IDataBoundRefType<TBoundTo> {
            bool rv = TryGetBoundRef(typeof(TBoundTo), typeof(T), id, out IDataBoundRefType value_);
            value = (T) value_;
            return rv;
        }

        public bool TryGetBoundRef(Type typeBoundTo, Type type, uint id, out IDataBoundRefType value) {
            if (id == uint.MaxValue) {
                value = null;
                return true;
            }

            if (Bound.TryGetValue(typeBoundTo, out Dictionary<uint, Dictionary<Type, IDataBoundRefType>> boundByID) &&
                boundByID.TryGetValue(id, out Dictionary<Type, IDataBoundRefType> boundByType) &&
                boundByType.TryGetValue(type, out value)) {
                return true;
            }

            value = null;
            return false;
        }

        public T[] GetRefs<T>() where T : DataType<T>, IDataRefType
            => GetRefs(typeof(T)).Cast<T>().ToArray();

        public IDataRefType[] GetRefs(Type type) {
            if (References.TryGetValue(type, out Dictionary<uint, IDataRefType> refs))
                return refs.Values.ToArray();
            return new IDataRefType[0];
        }

        public IDataBoundRefType[] GetBoundRefs<TBoundTo>(TBoundTo boundTo) where TBoundTo : DataType<TBoundTo>, IDataRefType
            => GetBoundRefs<TBoundTo>(boundTo.ID);

        public IDataBoundRefType[] GetBoundRefs<TBoundTo>(uint id) where TBoundTo : DataType<TBoundTo>, IDataRefType
            => GetBoundRefs(typeof(TBoundTo), id);

        public IDataBoundRefType[] GetBoundRefs(Type typeBoundTo, uint id) {
            if (Bound.TryGetValue(typeBoundTo, out Dictionary<uint, Dictionary<Type, IDataBoundRefType>> boundByID) &&
                boundByID.TryGetValue(id, out Dictionary<Type, IDataBoundRefType> boundByType))
                return boundByType.Values.ToArray();
            return new IDataBoundRefType[0];
        }

        public void SetRef(IDataRefType data)
            => SetRef(data.GetType(), data);

        public T SetRef<T>(T data) where T : DataType<T>, IDataRefType
            => (T) SetRef(typeof(T), data);

        public IDataRefType SetRef(Type type, IDataRefType data) {
            if (data == null)
                return null;

            uint id = data.ID;

            if (!data.IsAliveRef) {
                FreeRef(type, id);
                return null;
            }

            if (!References.TryGetValue(type, out Dictionary<uint, IDataRefType> refs)) {
                refs = new Dictionary<uint, IDataRefType>();
                References[type] = refs;
            }

            if (data is IDataBoundRefType bound) {
                Type typeBoundTo = data.GetType()
                    .GetInterfaces()
                    .FirstOrDefault(t => t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(IDataBoundRefType<>))
                    ?.GetGenericArguments()[0];

                if (typeBoundTo != null) {
                    if (!TryGetRef(typeBoundTo, id, out _))
                        throw new Exception($"Cannot bind {type.FullName} to unknown reference {typeBoundTo.FullName} ID {id}");

                    if (!Bound.TryGetValue(typeBoundTo, out Dictionary<uint, Dictionary<Type, IDataBoundRefType>> boundByID)) {
                        boundByID = new Dictionary<uint, Dictionary<Type, IDataBoundRefType>>();
                        Bound[typeBoundTo] = boundByID;
                    }

                    if (!boundByID.TryGetValue(id, out Dictionary<Type, IDataBoundRefType> boundByType)) {
                        boundByType = new Dictionary<Type, IDataBoundRefType>();
                        boundByID[id] = boundByType;
                    }

                    boundByType[type] = bound;
                }
            }

            return refs[data.ID] = data;
        }

        public void FreeRef<T>(uint id) where T : DataType<T>, IDataRefType
            => FreeRef(typeof(T), id);

        public void FreeRef(Type type, uint id) {
            IDataRefType data = null;
            if (References.TryGetValue(type, out Dictionary<uint, IDataRefType> refs) &&
                refs.TryGetValue(id, out data)) {
                refs.Remove(id);
            }

            if (Bound.TryGetValue(type, out Dictionary<uint, Dictionary<Type, IDataBoundRefType>> boundByID) &&
                boundByID.TryGetValue(id, out Dictionary<Type, IDataBoundRefType> boundByType)) {
                boundByID.Remove(id);
                foreach (Type typeBound in boundByType.Keys)
                    FreeRef(typeBound, id);
            }

            if (data is IDataBoundRefType bound) {
                Type typeBoundTo = bound.GetTypeBoundTo();
                if (typeBoundTo != null &&
                    Bound.TryGetValue(typeBoundTo, out boundByID) &&
                    boundByID.TryGetValue(id, out boundByType)) {
                    boundByID.Remove(id);
                    foreach (Type typeBound in boundByType.Keys)
                        FreeRef(typeBound, id);
                }
            }
        }

    }
}
