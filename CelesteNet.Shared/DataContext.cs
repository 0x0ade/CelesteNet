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
    public class DataContext : IDisposable {

        public readonly Dictionary<string, Type> IDToDataType = new();
        public readonly Dictionary<Type, string> DataTypeToID = new();
        public readonly Dictionary<Type, string> DataTypeToSource = new();

        public readonly Dictionary<string, Type> IDToMetaType = new ();
        public readonly Dictionary<Type, string> MetaTypeToID = new();

        public readonly object HandlersLock = new();
        public readonly object FiltersLock = new();

        public readonly ConcurrentDictionary<Type, DataHandler> Handlers = new();
        public readonly ConcurrentDictionary<Type, DataFilter> Filters = new();

        private readonly ConcurrentDictionary<object, List<Tuple<Type, DataHandler>>> RegisteredHandlers = new();
        private readonly ConcurrentDictionary<object, List<Tuple<Type, DataFilter>>> RegisteredFilters = new();

        protected readonly ConcurrentDictionary<string, ConcurrentDictionary<uint, DataType>> References = new();
        protected readonly ConcurrentDictionary<string, ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>> Bound = new();

        protected readonly ConcurrentDictionary<Type, ConcurrentDictionary<uint, byte>> LastOrderedUpdate = new();
        private bool IsDisposed;

        public DataContext() {
            RescanAllDataTypes();
        }

        public void RescanAllDataTypes() {
            Logger.Log(LogLevel.INF, "data", "Rescanning all data types");
            IDToDataType.Clear();
            DataTypeToID.Clear();

            RescanDataTypes(CelesteNetUtils.GetTypes());
        }

        public void RescanDataTypes(Type[] types) {
            foreach (Type type in types) {
                if (type.IsAbstract)
                    continue;

                if (typeof(DataType).IsAssignableFrom(type)) {
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

                    if (IDToDataType.ContainsKey(id)) {
                        Logger.Log(LogLevel.WRN, "data", $"Found data type {type.FullName} but conflicting ID {id}");
                        continue;
                    }

                    Logger.Log(LogLevel.INF, "data", $"Found data type {type.FullName} with ID {id}");
                    IDToDataType[id] = type;
                    DataTypeToID[type] = id;
                    DataTypeToSource[type] = source;

                } else if (typeof(MetaType).IsAssignableFrom(type)) {
                    RuntimeHelpers.RunClassConstructor(type.TypeHandle);

                    string? id = null;
                    for (Type parent = type; parent != typeof(object) && id.IsNullOrEmpty(); parent = parent.BaseType ?? typeof(object)) {
                        id = parent.GetField("MetaID", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                    }

                    if (id.IsNullOrEmpty()) {
                        Logger.Log(LogLevel.WRN, "data", $"Found meta type {type.FullName} but no MetaID");
                        continue;
                    }

                    if (IDToMetaType.ContainsKey(id)) {
                        Logger.Log(LogLevel.WRN, "data", $"Found meta type {type.FullName} but conflicting ID {id}");
                        continue;
                    }

                    Logger.Log(LogLevel.INF, "data", $"Found meta type {type.FullName} with ID {id}");
                    IDToMetaType[id] = type;
                    MetaTypeToID[type] = id;
                }
            }
        }

        public void RemoveDataTypes(Type[] types) {
            foreach (Type type in types) {
                if (!DataTypeToID.TryGetValue(type, out string? id))
                    continue;

                Logger.Log(LogLevel.INF, "data", $"Removing data type {type.FullName} with ID {id}");
                IDToDataType.Remove(id);
                DataTypeToID.Remove(type);
            }
        }

        public DataHandler RegisterHandler<T>(DataHandler<T> handler) where T : DataType<T> {
            DataHandler wrap = (con, data) => handler(con, (T) data);
            RegisterHandler(typeof(T), wrap);
            return wrap;
        }

        public void RegisterHandler(Type type, DataHandler handler) {
            lock (HandlersLock) {
                if (Handlers.TryGetValue(type, out DataHandler? existing))
                    handler = existing + handler;
                Handlers[type] = handler;
            }
        }

        public DataFilter RegisterFilter<T>(DataFilter<T> filter) where T : DataType<T> {
            DataFilter wrap = (con, data) => filter(con, (T) data);
            RegisterFilter(typeof(T), wrap);
            return wrap;
        }

        public void RegisterFilter(Type type, DataFilter filter) {
            lock (FiltersLock) {
                if (Filters.TryGetValue(type, out DataFilter? existing))
                    filter = existing + filter;
                Filters[type] = filter;
            }
        }

        public void UnregisterHandler(Type type, DataHandler handler) {
            lock (HandlersLock) {
                if (Handlers.TryGetValue(type, out DataHandler? existing)) {
                    existing -= handler;
                    if (existing != null)
                        Handlers[type] = existing;
                    else
                        Handlers.TryRemove(type, out _);
                }
            }
        }

        public void UnregisterFilter(Type type, DataFilter filter) {
            lock (FiltersLock) {
                if (Filters.TryGetValue(type, out DataFilter? existing)) {
                    existing -= filter;
                    if (existing != null)
                        Filters[type] = existing;
                    else
                        Filters.TryRemove(type, out _);
                }
            }
        }

        public void RegisterHandlersIn(object owner) {
            if (RegisteredHandlers.ContainsKey(owner))
                return;

            List<Tuple<Type, DataHandler>> handlers = RegisteredHandlers[owner] = new();
            List<Tuple<Type, DataFilter>> filters = RegisteredFilters[owner] = new();

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
            if (RegisteredHandlers.TryRemove(owner, out List<Tuple<Type, DataHandler>>? handlers))
                foreach (Tuple<Type, DataHandler> tuple in handlers)
                    UnregisterHandler(tuple.Item1, tuple.Item2);

            if (RegisteredFilters.TryRemove(owner, out List<Tuple<Type, DataFilter>>? filters))
                foreach (Tuple<Type, DataFilter> tuple in filters)
                    UnregisterFilter(tuple.Item1, tuple.Item2);
        }

        public Action WaitFor<T>(DataFilter<T> cb) where T : DataType<T>
            => WaitFor(0, cb, null);

        public Action WaitFor<T>(int timeout, DataFilter<T> cb, Action? cbTimeout = null) where T : DataType<T> {
            object key = new();

            DataHandler? wrap = null;
            wrap = RegisterHandler<T>((con, data) => {
                lock (key) {
                    if (wrap == null || !cb(con, data))
                        return;
                    UnregisterHandler(typeof(T), wrap);
                    wrap = null;
                }
            });

            if (timeout > 0)
                Task.Run(async () => {
                    await Task.Delay(timeout);
                    lock (key) {
                        if (wrap == null)
                            return;
                        try {
                            UnregisterHandler(typeof(T), wrap);
                            wrap = null;
                            cbTimeout?.Invoke();
                        } catch (Exception e) {
                            Logger.Log(LogLevel.CRI, "data", $"Error in WaitFor timeout callback:\n{typeof(T).FullName}\n{cb}\n{e}");
                        }
                    }
                });

            return () => UnregisterHandler(typeof(T), wrap);
        }

        public DataType Read(CelesteNetBinaryReader reader) {
            PositionAwareStream? pas = reader.BaseStream as PositionAwareStream;
            pas?.ResetPosition();

            DataFlags flags = (DataFlags) reader.ReadUInt16();
            if ((flags & DataFlags.InteralSlimIndicator) != 0) {
                if (reader.SlimMap == null)
                    throw new InvalidDataException("Trying to read a slim packet header without a slim map!");

                Type slimType = reader.SlimMap.Get((int) (flags & ~DataFlags.InteralSlimIndicator));
                DataType? slimData = Activator.CreateInstance(slimType) as DataType;
                if (slimData == null)
                    throw new InvalidDataException($"Cannot create instance of data type {slimType.FullName}");
                
                slimData.ReadAll(reader);

                return slimData;
            }

            string id = reader.ReadNetMappedString();
            string source = reader.ReadNetMappedString();
            uint length = ((flags & DataFlags.Small) != 0) ? reader.ReadByte() : reader.ReadUInt16();
            long start = pas?.Position ?? 0;

            if (!IDToDataType.TryGetValue(id, out Type? type))
                return new DataUnparsed() {
                    InnerID = id,
                    InnerSource = source,
                    InnerFlags = flags,
                    InnerData = reader.ReadBytes((int) length)
                };

            DataType? data = Activator.CreateInstance(type) as DataType;
            if (data == null)
                throw new InvalidOperationException($"Cannot create instance of DataType '{type.FullName}'");

            data.ReadAll(reader);

            if (pas != null) {
                long lengthReal = pas.Position - start;
                if (lengthReal != length)
                    throw new InvalidDataException($"Length mismatch for DataType '{id}' {flags} {source} {length} - got {lengthReal}");
            }

            if ((flags & DataFlags.SlimHeader) != 0)
                reader.SlimMap?.CountRead(type);

            return data;
        }

        public MetaType ReadMeta(CelesteNetBinaryReader reader) {
            PositionAwareStream? pas = reader.BaseStream as PositionAwareStream;
            pas?.ResetPosition();

            string id = reader.ReadNetMappedString();
            uint length = reader.ReadByte();
            long start = pas?.Position ?? 0;

            if (!IDToMetaType.TryGetValue(id, out Type? type))
                return new MetaUnparsed() {
                    InnerID = id,
                    InnerData = reader.ReadBytes((int) length)
                };

            MetaType? meta = Activator.CreateInstance(type) as MetaType;
            if (meta == null)
                throw new InvalidOperationException($"Cannot create instance of MetaType '{type.FullName}'");

            meta.Read(reader);

            if (pas != null) {
                long lengthReal = pas.Position - start;
                if (lengthReal != length)
                    throw new InvalidDataException($"Length mismatch for MetaType '{id}' {length} - got {lengthReal}");
            }

            return meta;
        }

        public int Write(CelesteNetBinaryWriter writer, DataType data) {
            long start = writer.BaseStream.Position;

            if (writer.SlimMap != null && writer.TryGetSlimID(data.GetType(), out int slimID)) {
                writer.Write((ushort) (slimID | (ushort) DataFlags.InteralSlimIndicator));
                data.WriteAll(writer);
                return (int) (writer.BaseStream.Position - start);
            }

            DataFlags flags = data.DataFlags;
            if ((flags & (DataFlags.RESERVED | DataFlags.InteralSlimIndicator)) != 0)
                Logger.Log(LogLevel.WRN, "datactx", $"DataType {data} has reserved flags set");
            flags &= ~(DataFlags.RESERVED | DataFlags.InteralSlimIndicator);

            bool small = (flags & DataFlags.Small) != 0;

            writer.Write((ushort) flags);
            writer.WriteNetMappedString(data.GetTypeID(this));
            writer.WriteNetMappedString(data.GetSource(this));

            writer.Flush();
            long lenPos = writer.BaseStream.Position;
            if (small)
                writer.Write((byte) 0);
            else
                writer.Write((ushort) 0);

            data.WriteAll(writer);

            writer.Flush();
            long end = writer.BaseStream.Position;
            long len = end - (lenPos + (small ? 1 : 2));
            writer.BaseStream.Position = lenPos;
            if (small)
                writer.Write((byte) len);
            else
                writer.Write((ushort) len);
            writer.Flush();
            writer.BaseStream.Position = end;

            return (int) (end - start);
        }

        public int WriteMeta(CelesteNetBinaryWriter writer, MetaType meta) {
            long start = writer.BaseStream.Position;

            writer.WriteNetMappedString(meta.GetTypeID(this));

            writer.Flush();
            long lenPos = writer.BaseStream.Position;
            writer.Write((byte) 0);

            meta.Write(writer);

            writer.Flush();
            long end = writer.BaseStream.Position;
            long len = end - (lenPos + 1);
            writer.BaseStream.Position = lenPos;
            writer.Write((byte) len);
            writer.Flush();
            writer.BaseStream.Position = end;

            return (int) (end - start);
        }

        public void Handle(CelesteNetConnection con, DataType? data)
            => Handle(con, data?.GetType(), data);

        public void Handle<T>(CelesteNetConnection con, T? data) where T : DataType<T>
            => Handle(con, typeof(T), data);

        protected void Handle(CelesteNetConnection con, Type? type, DataType? data) {
            if (type == null || data == null)
                return;

            if ((data.DataFlags & DataFlags.Taskable) == DataFlags.Taskable) {
                Task.Run(() => {
                    try {
                        HandleInner(con, type, data);
                    } catch (Exception e) {
                        Logger.Log(LogLevel.CRI, "data-task", $"Failed handling data in task:\n{con}\n{type.FullName}\n{e}");
                    }
                });
                return;
            }

            HandleInner(con, type, data);
        }

        protected void HandleInner(CelesteNetConnection con, Type type, DataType data) {
            for (Type btype = type; btype != typeof(object); btype = btype.BaseType ?? typeof(object))
                if (Filters.TryGetValue(btype, out DataFilter? filter))
                    if (!filter.InvokeWhileTrue(con, data))
                        return;

            if (!data.FilterHandle(this))
                return;

            if (data.TryGet(this, out MetaOrderedUpdate? update) && update.UpdateID != null) {
                if (!LastOrderedUpdate.TryGetValue(type, out ConcurrentDictionary<uint, byte>? updateIDs)) {
                    updateIDs = new();
                    LastOrderedUpdate[type] = updateIDs;
                }

                uint id = update.ID;
                byte updateID = update.UpdateID.Value;
                if (!updateIDs.TryGetValue(id, out byte updateIDLast)) {
                    updateIDLast = 0;
                }

                if ((updateID - updateIDLast + 256) % 256 < 128)
                    return;

                updateIDs[id] = updateID;
            }

            if (data.Is<MetaRef>(this))
                SetRef(data);

            if (data.Is<MetaBoundRef>(this))
                SetBoundRef(data);

            for (Type btype = type; btype != typeof(object); btype = btype.BaseType ?? typeof(object))
                if (Handlers.TryGetValue(btype, out DataHandler? handler))
                    handler(con, data);
        }


        [Obsolete("Use CelesteNetBinaryReader instead.")]
        public T? ReadRef<T>(BinaryReader reader) where T : DataType<T>
            => GetRef<T>(reader.ReadUInt32());

        [Obsolete("Use CelesteNetBinaryReader instead.")]
        public T? ReadOptRef<T>(BinaryReader reader) where T : DataType<T>
            => TryGetRef(reader.ReadUInt32(), out T? value) ? value : null;

        [Obsolete("Use CelesteNetBinaryWriter instead.")]
        public void WriteRef<T>(BinaryWriter writer, T? data) where T : DataType<T>
            => writer.Write((data ?? throw new Exception($"Expected {DataTypeToID[typeof(T)]} to write, got null")).Get<MetaRef>(this) ?? uint.MaxValue);

        [Obsolete("Use CelesteNetBinaryWriter instead.")]
        public void WriteOptRef<T>(BinaryWriter writer, T? data) where T : DataType<T>
            => writer.Write(data?.GetOpt<MetaRef>(this) ?? uint.MaxValue);


        public T? GetRef<T>(uint id) where T : DataType<T>
            => (T?) GetRef(DataTypeToID[typeof(T)], id);

        public DataType? GetRef(string type, uint id)
            => TryGetRef(type, id, out DataType? value) ? value : throw new Exception($"Unknown reference {type} ID {id}");

        public bool TryGetRef<T>(uint id, out T? value) where T : DataType<T> {
            bool rv = TryGetRef(DataTypeToID[typeof(T)], id, out DataType? value_);
            value = (T?) value_;
            return rv;
        }

        public bool TryGetRef(string type, uint id, out DataType? value) {
            if (id == uint.MaxValue) {
                value = null;
                return true;
            }

            if (References.TryGetValue(type, out ConcurrentDictionary<uint, DataType>? refs) &&
                refs.TryGetValue(id, out value)) {
                return true;
            }

            value = null;
            return false;
        }

        public T[] GetRefs<T>() where T : DataType<T>
            => GetRefs(DataTypeToID[typeof(T)]).Cast<T>().ToArray();

        public DataType[] GetRefs(string type) {
            if (References.TryGetValue(type, out ConcurrentDictionary<uint, DataType>? refs))
                return refs.Values.ToArray();
            return Dummy<DataType>.EmptyArray;
        }

        public T? GetBoundRef<TBoundTo, T>(uint id) where TBoundTo : DataType<TBoundTo> where T : DataType<T>
            => (T?) GetBoundRef(DataTypeToID[typeof(TBoundTo)], DataTypeToID[typeof(T)], id);

        public T? GetBoundRef<TBoundTo, T>(TBoundTo? boundTo) where TBoundTo : DataType<TBoundTo> where T : DataType<T>
            => (T?) GetBoundRef(DataTypeToID[typeof(TBoundTo)], DataTypeToID[typeof(T)], boundTo?.Get<MetaRef>(this) ?? uint.MaxValue);

        public DataType? GetBoundRef(string typeBoundTo, string type, uint id)
            => TryGetBoundRef(typeBoundTo, type, id, out DataType? value) ? value : throw new Exception($"Unknown reference {typeBoundTo} bound to {type} ID {id}");

        public bool TryGetBoundRef<TBoundTo, T>(TBoundTo? boundTo, out T? value) where TBoundTo : DataType<TBoundTo> where T : DataType<T>
            => TryGetBoundRef<TBoundTo, T>(boundTo?.Get<MetaRef>(this) ?? uint.MaxValue, out value);

        public bool TryGetBoundRef<TBoundTo, T>(uint id, out T? value) where TBoundTo : DataType<TBoundTo> where T : DataType<T> {
            bool rv = TryGetBoundRef(DataTypeToID[typeof(TBoundTo)], DataTypeToID[typeof(T)], id, out DataType? value_);
            value = (T?) value_;
            return rv;
        }

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


        public DataType[] GetBoundRefs<TBoundTo>(TBoundTo? boundTo) where TBoundTo : DataType<TBoundTo>
            => GetBoundRefs<TBoundTo>(boundTo?.Get<MetaRef>(this) ?? uint.MaxValue);

        public DataType[] GetBoundRefs<TBoundTo>(uint id) where TBoundTo : DataType<TBoundTo>
            => GetBoundRefs(DataTypeToID[typeof(TBoundTo)], id);

        public DataType[] GetBoundRefs(string typeBoundTo, uint id) {
            if (Bound.TryGetValue(typeBoundTo, out ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>? boundByID) &&
                boundByID.TryGetValue(id, out ConcurrentDictionary<string, DataType>? boundByType))
                return boundByType.Values.ToArray();
            return Dummy<DataType>.EmptyArray;
        }

        public DataType? SetRef(DataType? data) {
            if (data == null)
                return null;

            string type = data.GetTypeID(this);
            if (type.IsNullOrEmpty())
                return null;

            MetaRef metaRef = data.Get<MetaRef>(this);
            uint id = metaRef;

            if (!metaRef.IsAlive) {
                FreeRef(type, id);
                return null;
            }

            if (!References.TryGetValue(type, out ConcurrentDictionary<uint, DataType>? refs)) {
                refs = new();
                References[type] = refs;
            }

            return refs[id] = data;
        }

        public DataType? SetBoundRef(DataType? data) {
            if (data == null)
                return null;

            string typeBoundTo = data.Get<MetaBoundRef>(this).TypeBoundTo;
            string type = data.GetTypeID(this);
            if (typeBoundTo.IsNullOrEmpty() || type.IsNullOrEmpty())
                return null;

            MetaBoundRef metaBoundRef = data.Get<MetaBoundRef>(this);
            uint id = metaBoundRef.ID;

            if (!metaBoundRef.IsAlive) {
                FreeBoundRef(typeBoundTo, type, id);
                return null;
            }

            if (!TryGetRef(typeBoundTo, id, out _))
                throw new Exception($"Cannot bind {type} to unknown reference {typeBoundTo} ID {id}");

            if (!Bound.TryGetValue(typeBoundTo, out ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>? boundByID)) {
                boundByID = new();
                Bound[typeBoundTo] = boundByID;
            }

            if (!boundByID.TryGetValue(id, out ConcurrentDictionary<string, DataType>? boundByType)) {
                boundByType = new();
                boundByID[id] = boundByType;
            }

            return boundByType[type] = data;
        }

        public void FreeRef<T>(uint id) where T : DataType<T>
            => FreeRef(DataTypeToID[typeof(T)], id);

        public void FreeRef(DataType data)
            => FreeRef(data.GetTypeID(this), data.Get<MetaRef>(this));

        public void FreeRef(string type, uint id) {
            if (References.TryGetValue(type, out ConcurrentDictionary<uint, DataType>? refs))
                refs.TryRemove(id, out _);

            if (Bound.TryGetValue(type, out ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>? boundByID) &&
                boundByID.TryGetValue(id, out ConcurrentDictionary<string, DataType>? boundByType)) {
                boundByID.TryRemove(id, out _);
                foreach (string typeBound in boundByType.Keys)
                    FreeRef(typeBound, id);
            }
        }

        public void FreeBoundRef<TBoundTo, T>(uint id) where TBoundTo : DataType<TBoundTo> where T : DataType<T>
            => FreeBoundRef(DataTypeToID[typeof(TBoundTo)], DataTypeToID[typeof(T)], id);

        public void FreeBoundRef(DataType data) {
            MetaBoundRef bound = data.Get<MetaBoundRef>(this);
            FreeBoundRef(bound.TypeBoundTo, data.GetTypeID(this), bound.ID);
        }

        public void FreeBoundRef(string typeBoundTo, string type, uint id) {
            if (Bound.TryGetValue(typeBoundTo, out ConcurrentDictionary<uint, ConcurrentDictionary<string, DataType>>? boundByID) &&
                boundByID.TryGetValue(id, out ConcurrentDictionary<string, DataType>? boundByType))
                boundByType.TryRemove(type, out _);
        }

        public void FreeOrder<T>(uint id) where T : DataType<T>
            => FreeOrder(typeof(T), id);

        public void FreeOrder(Type type, uint id) {
            if (LastOrderedUpdate.TryGetValue(type, out ConcurrentDictionary<uint, byte>? updateIDs)) {
                updateIDs.TryRemove(id, out _);
            }
        }


        protected virtual void Dispose(bool disposing) {
            if (!IsDisposed)
                return;
            IsDisposed = true;
        }

        ~DataContext() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

    }
}
