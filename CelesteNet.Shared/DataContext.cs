using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
        public readonly Dictionary<Type, string> TypeToSourceMap = new Dictionary<Type, string>();
        public readonly Dictionary<Type, string[]> TypeToBehaviorsMap = new Dictionary<Type, string[]>();
        public readonly Dictionary<Type, string> TypeToBehaviorMap = new Dictionary<Type, string>();
        public readonly Dictionary<string, Type> BehaviorToTypeMap = new Dictionary<string, Type>();

        public readonly HashSet<Type> BuiltinTypes = new HashSet<Type>();

        public readonly ConcurrentDictionary<Type, DataHandler> Handlers = new ConcurrentDictionary<Type, DataHandler>();
        public readonly ConcurrentDictionary<Type, DataFilter> Filters = new ConcurrentDictionary<Type, DataFilter>();

        private readonly ConcurrentDictionary<object, List<Tuple<Type, DataHandler>>> RegisteredHandlers = new ConcurrentDictionary<object, List<Tuple<Type, DataHandler>>>();
        private readonly ConcurrentDictionary<object, List<Tuple<Type, DataFilter>>> RegisteredFilters = new ConcurrentDictionary<object, List<Tuple<Type, DataFilter>>>();

        protected readonly ConcurrentDictionary<Type, IDataStatic> Static = new ConcurrentDictionary<Type, IDataStatic>();

        protected readonly ConcurrentDictionary<string, ConcurrentDictionary<uint, IDataRef>> References = new ConcurrentDictionary<string, ConcurrentDictionary<uint, IDataRef>>();
        protected readonly ConcurrentDictionary<string, ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>> Bound = new ConcurrentDictionary<string, ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>>();

        protected readonly ConcurrentDictionary<Type, ConcurrentDictionary<uint, uint>> LastOrderedUpdate = new ConcurrentDictionary<Type, ConcurrentDictionary<uint, uint>>();

        public DataContext() {
            RescanAllDataTypes();
        }

        public void RescanAllDataTypes() {
            Logger.Log(LogLevel.INF, "data", "Rescanning all data types");
            IDToTypeMap.Clear();
            TypeToIDMap.Clear();

            RescanDataTypes(CelesteNetUtils.GetTypes());
        }

        public void RescanDataTypes(Type[] types) {
            foreach (Type type in types) {
                if (!typeof(DataType).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                string? id = null;
                string? source = null;
                for (Type parent = type; parent != typeof(object) && id.IsNullOrEmpty() && source.IsNullOrEmpty(); parent = parent.BaseType ?? typeof(object)) {
                    id = parent.GetField("DataID", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                    source = parent.GetField("DataSource", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                }

                if (id.IsNullOrEmpty()) {
                    Logger.Log(LogLevel.WRN, "data", $"Found data type {type.FullName} but no DataID");
                    continue;
                }

                if (source.IsNullOrEmpty()) {
                    Logger.Log(LogLevel.WRN, "data", $"Found data type {type.FullName} but no DataSource");
                    continue;
                }

                if (IDToTypeMap.ContainsKey(id)) {
                    Logger.Log(LogLevel.WRN, "data", $"Found data type {type.FullName} but conflicting ID {id}");
                    continue;
                }

                Logger.Log(LogLevel.INF, "data", $"Found data type {type.FullName} with ID {id}");
                IDToTypeMap[id] = type;
                TypeToIDMap[type] = id;
                TypeToSourceMap[type] = source;

                List<string> behaviors = new List<string>();

                foreach (Type interf in type.GetInterfaces()) {
                    DataBehaviorAttribute? behavior = interf?.GetCustomAttribute<DataBehaviorAttribute>();
                    if (behavior == null)
                        continue;
                    string? bid = behavior.ID;
                    if (bid.IsNullOrEmpty())
                        bid = interf.FullName ?? throw new NullReferenceException($"Typeless name: {interf}");
                    behaviors.Add(bid);
                    TypeToBehaviorMap[interf] = bid;
                    BehaviorToTypeMap[bid] = interf;
                }

                TypeToBehaviorsMap[type] = behaviors.ToArray();

                if (type.Assembly == typeof(DataContext).Assembly)
                    BuiltinTypes.Add(type);
            }
        }

        public void RemoveDataTypes(Type[] types) {
            foreach (Type type in types) {
                if (!TypeToIDMap.TryGetValue(type, out string? id))
                    continue;

                Logger.Log(LogLevel.INF, "data", $"Removing data type {type.FullName} with ID {id}");
                IDToTypeMap.Remove(id);
                TypeToIDMap.Remove(type);
            }
        }

        public void RegisterHandler<T>(DataHandler<T> handler) where T : DataType<T>
            => RegisterHandler(typeof(T), (con, data) => handler(con, (T) data));

        public void RegisterHandler(Type type, DataHandler handler) {
            if (Handlers.TryGetValue(type, out DataHandler? existing))
                handler = existing + handler;
            Handlers[type] = handler;
        }

        public void RegisterFilter<T>(DataFilter<T> filter) where T : DataType<T>
            => RegisterFilter(typeof(T), (con, data) => filter(con, (T) data));

        public void RegisterFilter(Type type, DataFilter filter) {
            if (Filters.TryGetValue(type, out DataFilter? existing))
                filter = existing + filter;
            Filters[type] = filter;
        }

        public void UnregisterHandler<T>(DataHandler<T> handler) where T : DataType<T>
            => UnregisterHandler(typeof(T), (con, data) => handler(con, (T) data));

        public void UnregisterHandler(Type type, DataHandler handler) {
            if (Handlers.TryGetValue(type, out DataHandler? existing)) {
                existing -= handler;
                if (existing != null)
                    Handlers[type] = existing;
                else
                    Handlers.TryRemove(type, out _);
            }
        }

        public void UnregisterFilter<T>(DataFilter<T> filter) where T : DataType<T>
            => UnregisterFilter(typeof(T), (con, data) => filter(con, (T) data));

        public void UnregisterFilter(Type type, DataFilter filter) {
            if (Filters.TryGetValue(type, out DataFilter? existing)) {
                existing -= filter;
                if (existing != null)
                    Filters[type] = existing;
                else
                    Filters.TryRemove(type, out _);
            }
        }

        public void RegisterHandlersIn(object owner) {
            if (RegisteredHandlers.ContainsKey(owner))
                return;

            List<Tuple<Type, DataHandler>> handlers = RegisteredHandlers[owner] = new List<Tuple<Type, DataHandler>>();
            List<Tuple<Type, DataFilter>> filters = RegisteredFilters[owner] = new List<Tuple<Type, DataFilter>>();

            foreach (MethodInfo method in owner.GetType().GetMethods()) {
                if (method.Name == "Handle" || method.Name == "Filter") {
                    ParameterInfo[] args = method.GetParameters();
                    if (args.Length != 2 || !args[0].ParameterType.IsCompatible(typeof(CelesteNetConnection)))
                        continue;

                    Type argType = args[1].ParameterType;
                    if (!argType.IsCompatible(typeof(DataType)))
                        continue;

                    if (method.Name == "Filter") {
                        Logger.Log(LogLevel.VVV, "data", $"Autoregistering filter for {argType}: {method.GetID()}");
                        DataFilter filter = (con, data) => method.Invoke(owner, new object[] { con, data }) as bool? ?? false;
                        filters.Add(Tuple.Create(argType, filter));
                        RegisterFilter(argType, filter);

                    } else {
                        Logger.Log(LogLevel.VVV, "data", $"Autoregistering handler for {argType}: {method.GetID()}");
                        DataHandler handler = (con, data) => method.Invoke(owner, new object[] { con, data });
                        handlers.Add(Tuple.Create(argType, handler));
                        RegisterHandler(argType, handler);
                    }
                }
            }
        }

        public void UnregisterHandlersIn(object owner) {
            if (!RegisteredHandlers.ContainsKey(owner))
                return;

            foreach (Tuple<Type, DataHandler> tuple in RegisteredHandlers[owner])
                UnregisterHandler(tuple.Item1, tuple.Item2);

            foreach (Tuple<Type, DataFilter> tuple in RegisteredFilters[owner])
                UnregisterFilter(tuple.Item1, tuple.Item2);
        }

        public Action WaitFor<T>(DataFilter<T> cb) where T : DataType<T>
            => WaitFor(0, cb, null);

        public Action WaitFor<T>(int timeout, DataFilter<T> cb, Action? cbTimeout = null) where T : DataType<T> {
            object key = new object();

            DataHandler<T>? handler = null;

            handler = (con, data) => {
                lock (key) {
                    if (handler == null || !cb(con, data))
                        return;
                    UnregisterHandler(handler);
                    handler = null;
                }
            };

            RegisterHandler(handler);
            if (timeout > 0)
                Task.Run(async () => {
                    await Task.Delay(timeout);
                    lock (key) {
                        if (handler == null)
                            return;
                        try {
                            UnregisterHandler(handler);
                            handler = null;
                            cbTimeout?.Invoke();
                        } catch (Exception e) {
                            Logger.Log(LogLevel.CRI, "data", $"Error in WaitFor timeout callback:\n{typeof(T).FullName}\n{cb}\n{e}");
                        }
                    }
                });

            return () => UnregisterHandler(handler);
        }

        public DataType Read(BinaryReader reader) {
            string id = Calc.ReadNullTerminatedString(reader);
            DataFlags flags = (DataFlags) reader.ReadUInt16();
            bool small = (flags & DataFlags.Small) == DataFlags.Small;
            bool big = (flags & DataFlags.Big) == DataFlags.Big;
            uint length = small ? reader.ReadByte() : big ? reader.ReadUInt32() : reader.ReadUInt16();

            string source = "";
            string[] behaviors = Dummy<string>.EmptyArray;
            if ((flags & DataFlags.ModdedType) == DataFlags.ModdedType) {
                source = Calc.ReadNullTerminatedString(reader);
                behaviors = new string[reader.ReadByte()];
                for (int i = 0; i < behaviors.Length; i++)
                    behaviors[i] = Calc.ReadNullTerminatedString(reader);
            }

            if (!IDToTypeMap.TryGetValue(id, out Type? type))
                return new DataUnparsed() {
                    InnerID = id,
                    InnerSource = source,
                    InnerFlags = flags,
                    Behaviors = behaviors,
                    InnerData = reader.ReadBytes((int) length)
                };

            DataType? data = (DataType?) Activator.CreateInstance(type);
            if (data == null)
                throw new Exception($"Cannot create instance of data type {type.FullName}");
            data.Read(this, reader);
            return data;
        }

        public int Write(BinaryWriter writer, DataType data)
            => Write(writer, data.GetType(), data);

        public int Write<T>(BinaryWriter writer, T data) where T : DataType<T>
            => Write(writer, typeof(T), data);

        protected int Write(BinaryWriter writer, Type type, DataType data) {
            if (!TypeToIDMap.TryGetValue(type, out string? id))
                throw new Exception($"Unknown data type {type} ({data})");

            DataFlags flags = data.DataFlags;
            if (!BuiltinTypes.Contains(type))
                flags |= DataFlags.ModdedType;

            long startAll = writer.BaseStream.Position;
            bool small = (flags & DataFlags.Small) == DataFlags.Small;
            bool big = (flags & DataFlags.Big) == DataFlags.Big;

            writer.WriteNullTerminatedString(id);
            writer.Write((ushort) flags);
            if (small)
                writer.Write((byte) 0); // Filled in later.
            else if (big)
                writer.Write((uint) 0); // Filled in later.
            else
                writer.Write((ushort) 0); // Filled in later.
            writer.Flush();

            long startData = writer.BaseStream.Position;

            if ((flags & DataFlags.ModdedType) == DataFlags.ModdedType) {
                writer.WriteNullTerminatedString(TypeToSourceMap[type]);
                string[] behaviors = TypeToBehaviorsMap[type];
                writer.Write((byte) behaviors.Length);
                foreach (string behavior in behaviors)
                    writer.WriteNullTerminatedString(behavior);
            }

            data.Write(this, writer);
            writer.Flush();

            long end = writer.BaseStream.Position;
            long length = end - startData;

            if (small) {
                writer.BaseStream.Seek(startData - 1, SeekOrigin.Begin);
                if (length > byte.MaxValue)
                    length = byte.MaxValue;
                writer.Write((byte) length);

            } else if (big) {
                writer.BaseStream.Seek(startData - 4, SeekOrigin.Begin);
                if (length > uint.MaxValue)
                    length = uint.MaxValue;
                writer.Write((uint) length);

            } else {
                writer.BaseStream.Seek(startData - 2, SeekOrigin.Begin);
                if (length > ushort.MaxValue)
                    length = ushort.MaxValue;
                writer.Write((ushort) length);
            }
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

        public void Handle(CelesteNetConnection con, DataType? data)
            => Handle(con, data?.GetType(), data);

        public void Handle<T>(CelesteNetConnection con, T? data) where T : DataType<T>
            => Handle(con, typeof(T), data);

        protected void Handle(CelesteNetConnection con, Type? type, DataType? data) {
            if (type == null || data == null)
                return;

            for (Type btype = type; btype != typeof(object); btype = btype.BaseType ?? typeof(object))
                if (Filters.TryGetValue(btype, out DataFilter? filter))
                    if (!filter.InvokeWhileTrue(con, data))
                        return;

            if (!data.FilterHandle(this))
                return;

            if (data is IDataOrderedUpdate update) {
                if (!LastOrderedUpdate.TryGetValue(type, out ConcurrentDictionary<uint, uint>? updateIDs)) {
                    updateIDs = new ConcurrentDictionary<uint, uint>();
                    LastOrderedUpdate[type] = updateIDs;
                }

                uint id = update.ID;
                uint updateID = update.UpdateID;
                if (!updateIDs.TryGetValue(id, out uint updateIDLast)) {
                    updateIDLast = 0;
                }

                if (updateID < updateIDLast)
                    return;

                updateIDs[id] = updateID;
            }

            if (data is IDataRef dataRef)
                SetRef(dataRef);

            for (Type btype = type; btype != typeof(object); btype = btype.BaseType ?? typeof(object))
                if (Handlers.TryGetValue(btype, out DataHandler? handler))
                    handler(con, data);
        }

        public IDataStatic[] GetAllStatic()
            => Static.Values.ToArray();

        public T GetStatic<T>() where T : DataType<T>, IDataStatic
            => (T) GetStatic(typeof(T));

        public IDataStatic GetStatic(Type type)
            => Static.TryGetValue(type, out IDataStatic? value) ? value : throw new Exception($"Unknown Static {type.FullName}");

        public void SetStatic(IDataStatic data)
            => SetStatic(data.GetType(), data);

        public T SetStatic<T>(T data) where T : DataType<T>, IDataStatic
            => (T) SetStatic(typeof(T), data);

        public IDataStatic SetStatic(Type type, IDataStatic data)
            => Static[type] = data;

        public T? ReadRef<T>(BinaryReader reader) where T : DataType<T>, IDataRef
            => GetRef<T>(reader.ReadUInt32());

        public T? ReadOptRef<T>(BinaryReader reader) where T : DataType<T>, IDataRef
            => TryGetRef(reader.ReadUInt32(), out T? value) ? value : null;

        public void WriteRef<T>(BinaryWriter writer, T? data) where T : DataType<T>, IDataRef
            => writer.Write(data?.ID ?? uint.MaxValue);

        public T? GetRef<T>(uint id) where T : DataType<T>, IDataRef
            => (T?) GetRef(typeof(T), id);

        public IDataRef? GetRef(Type type, uint id)
            => TryGetRef(type, id, out IDataRef? value) ? value : throw new Exception($"Unknown reference {type.FullName} ID {id}");

        public bool TryGetRef<T>(uint id, out T? value) where T : DataType<T>, IDataRef {
            bool rv = TryGetRef(typeof(T), id, out IDataRef? value_);
            value = (T?) value_;
            return rv;
        }

        public bool TryGetRef(Type type, uint id, out IDataRef? value)
            => TryGetRef(TypeToIDMap[type], id, out value);

        public bool TryGetRef(string type, uint id, out IDataRef? value) {
            if (id == uint.MaxValue) {
                value = null;
                return true;
            }

            if (References.TryGetValue(type, out ConcurrentDictionary<uint, IDataRef>? refs) &&
                refs.TryGetValue(id, out value)) {
                return true;
            }

            value = null;
            return false;
        }

        public T[] GetRefs<T>() where T : DataType<T>, IDataRef
            => GetRefs(typeof(T)).Cast<T>().ToArray();

        public IDataRef[] GetRefs(Type type)
            => GetRefs(TypeToIDMap[type]);

        public IDataRef[] GetRefs(string type) {
            if (References.TryGetValue(type, out ConcurrentDictionary<uint, IDataRef>? refs))
                return refs.Values.ToArray();
            return new IDataRef[0];
        }

        public T? GetBoundRef<TBoundTo, T>(uint id) where TBoundTo : DataType<TBoundTo>, IDataRef where T : DataType<T>, IDataBoundRef<TBoundTo>
            => (T?) GetBoundRef(typeof(TBoundTo), typeof(T), id);

        public T? GetBoundRef<TBoundTo, T>(TBoundTo? boundTo) where TBoundTo : DataType<TBoundTo>, IDataRef where T : DataType<T>, IDataBoundRef<TBoundTo>
            => (T?) GetBoundRef(typeof(TBoundTo), typeof(T), boundTo?.ID ?? uint.MaxValue);

        public DataType? GetBoundRef(Type typeBoundTo, Type type, uint id)
            => TryGetBoundRef(typeBoundTo, type, id, out DataType? value) ? value : throw new Exception($"Unknown reference {typeBoundTo.FullName} bound to {type.FullName} ID {id}");

        public bool TryGetBoundRef<TBoundTo, T>(TBoundTo? boundTo, out T? value) where TBoundTo : DataType<TBoundTo>, IDataRef where T : DataType<T>, IDataBoundRef<TBoundTo>
            => TryGetBoundRef<TBoundTo, T>(boundTo?.ID ?? uint.MaxValue, out value);

        public bool TryGetBoundRef<TBoundTo, T>(uint id, out T? value) where TBoundTo : DataType<TBoundTo>, IDataRef where T : DataType<T>, IDataBoundRef<TBoundTo> {
            bool rv = TryGetBoundRef(typeof(TBoundTo), typeof(T), id, out DataType? value_);
            value = (T?) value_;
            return rv;
        }

        public bool TryGetBoundRef(Type typeBoundTo, Type type, uint id, out DataType? value)
            => TryGetBoundRef(TypeToIDMap[typeBoundTo], TypeToIDMap[type], id, out value);

        public bool TryGetBoundRef(string typeBoundTo, string type, uint id, out DataType? value) {
            if (id == uint.MaxValue) {
                value = null;
                return true;
            }

            if (Bound.TryGetValue(typeBoundTo, out ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>? boundByID) &&
                boundByID.TryGetValue(id, out ConcurrentDictionary<string, DataType>? boundByType) &&
                boundByType.TryGetValue(type, out value)) {
                return true;
            }

            value = null;
            return false;
        }


        public DataType[] GetBoundRefs<TBoundTo>(TBoundTo? boundTo) where TBoundTo : DataType<TBoundTo>, IDataRef
            => GetBoundRefs<TBoundTo>(boundTo?.ID ?? uint.MaxValue);

        public DataType[] GetBoundRefs<TBoundTo>(uint id) where TBoundTo : DataType<TBoundTo>, IDataRef
            => GetBoundRefs(typeof(TBoundTo), id);

        public DataType[] GetBoundRefs(Type typeBoundTo, uint id)
            => GetBoundRefs(TypeToIDMap[typeBoundTo], id);

        public DataType[] GetBoundRefs(string typeBoundTo, uint id) {
            if (Bound.TryGetValue(typeBoundTo, out ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>? boundByID) &&
                boundByID.TryGetValue(id, out ConcurrentDictionary<string, DataType>? boundByType))
                return boundByType.Values.ToArray();
            return new DataType[0];
        }

        public void SetRef(IDataRef? data)
            => SetRef(data == null ? "" : TypeToIDMap[data.GetType()], data);

        public T? SetRef<T>(T? data) where T : DataType<T>, IDataRef
            => (T?) SetRef(typeof(T), data);

        public IDataRef? SetRef(Type type, IDataRef? data)
            => SetRef(TypeToIDMap[type], data);

        public IDataRef? SetRef(string type, IDataRef? data) {
            if (type.IsNullOrEmpty() || data == null)
                return null;

            uint id = data.ID;

            if (!data.IsAliveRef) {
                FreeRef(type, id);
                return null;
            }

            if (!References.TryGetValue(type, out ConcurrentDictionary<uint, IDataRef>? refs)) {
                refs = new ConcurrentDictionary<uint, IDataRef>();
                References[type] = refs;
            }

            if (data is IDataBoundRef bound) {
                Type? typeBoundToRaw = bound.GetBoundToType();

                if (typeBoundToRaw != null) {
                    string typeBoundTo = TypeToIDMap[typeBoundToRaw];
                    if (!TryGetRef(typeBoundTo, id, out _))
                        throw new Exception($"Cannot bind {type} to unknown reference {typeBoundTo} ID {id}");

                    if (!Bound.TryGetValue(typeBoundTo, out ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>? boundByID)) {
                        boundByID = new ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>();
                        Bound[typeBoundTo] = boundByID;
                    }

                    if (!boundByID.TryGetValue(id, out ConcurrentDictionary<string, DataType>? boundByType)) {
                        boundByType = new ConcurrentDictionary<string, DataType>();
                        boundByID[id] = boundByType;
                    }

                    boundByType[type] = (DataType) bound;
                }
            }

            return refs[data.ID] = data;
        }

        public void FreeRef<T>(uint id) where T : DataType<T>, IDataRef
            => FreeRef(typeof(T), id);

        public void FreeRef(Type type, uint id)
            => FreeRef(TypeToIDMap[type], id);

        public void FreeRef(string type, uint id) {
            IDataRef? data = null;
            if (References.TryGetValue(type, out ConcurrentDictionary<uint, IDataRef>? refs) &&
                refs.TryGetValue(id, out data)) {
                refs.TryRemove(id, out _);
            }

            if (Bound.TryGetValue(type, out ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>? boundByID) &&
                boundByID.TryGetValue(id, out ConcurrentDictionary<string, DataType>? boundByType)) {
                boundByID.TryRemove(id, out _);
                foreach (string typeBound in boundByType.Keys)
                    FreeRef(typeBound, id);
            }

            if (data is IDataBoundRef bound) {
                Type? typeBoundToRaw = bound.GetBoundToType();
                if (typeBoundToRaw != null &&
                    Bound.TryGetValue(TypeToIDMap[typeBoundToRaw], out boundByID) &&
                    boundByID.TryGetValue(id, out boundByType)) {
                    boundByID.TryRemove(id, out _);
                    foreach (string typeBound in boundByType.Keys)
                        FreeRef(typeBound, id);
                }
            }
        }

        public void FreeOrder<T>(uint id) where T : DataType<T>, IDataOrderedUpdate
            => FreeOrder(typeof(T), id);

        public void FreeOrder(Type type, uint id) {
            if (LastOrderedUpdate.TryGetValue(type, out ConcurrentDictionary<uint, uint>? updateIDs)) {
                updateIDs.TryRemove(id, out _);
            }
        }

    }
}
