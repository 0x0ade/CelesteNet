using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Monocle;

namespace Celeste.Mod.CelesteNet {
    public abstract class CelesteNetConnection : IDisposable {

        public readonly string Creator = "Unknown";
        public readonly DataContext Data;

        protected readonly object DisposeLock = new();

        private Action<CelesteNetConnection>? _OnDisconnect;
        public event Action<CelesteNetConnection> OnDisconnect {
            add {
                lock (DisposeLock) {
                    _OnDisconnect += value;
                    if (!IsAlive)
                        value?.Invoke(this);
                }
            }
            remove {
                lock (DisposeLock) {
                    _OnDisconnect -= value;
                }
            }
        }

        private readonly object SendFilterLock = new();
        private DataFilter? _OnSendFilter;
        public event DataFilter OnSendFilter {
            add {
                lock (SendFilterLock) {
                    _OnSendFilter += value;
                }
            }
            remove {
                lock (SendFilterLock) {
                    _OnSendFilter -= value;
                }
            }
        }

        private readonly object ReceiveFilterLock = new();
        private DataFilter? _OnReceiveFilter;
        public event DataFilter OnReceiveFilter {
            add {
                lock (ReceiveFilterLock) {
                    _OnReceiveFilter += value;
                }
            }
            remove {
                lock (ReceiveFilterLock) {
                    _OnReceiveFilter -= value;
                }
            }
        }

        public virtual bool IsAlive { get; protected set; } = true;
        public abstract bool IsConnected { get; }
        public abstract string ID { get; }
        public abstract string UID { get; }

        public bool SendKeepAlive = false;

        // TODO: Merge these features with the next protocol version bump.
        public bool SendStringMap = false;

        protected List<CelesteNetSendQueue> SendQueues = new();

        public readonly CelesteNetSendQueue DefaultSendQueue;

        public CelesteNetConnection(DataContext data) {
            Data = data;

            StackTrace trace = new();
            foreach (StackFrame? frame in trace.GetFrames()) {
                MethodBase? method = frame?.GetMethod();
                if (method == null || method.IsConstructor)
                    continue;

                string? type = method.DeclaringType?.Name;
                Creator = (type == null ? "" : type + "::") + method.Name;
                break;
            }

            SendQueues.Add(DefaultSendQueue = new(this, "") {
                SendKeepAliveUpdate = true,
                SendKeepAliveNonUpdate = true,
                SendStringMapUpdate = false
            });
        }

        public virtual void Send(DataType? data) {
            if (data == null)
                return;
            if (data is DataInternalLoopbackMessage msg) {
                LoopbackReceive(msg);
                return;
            }
            if (data is DataInternalLoopend end) {
                LoopbackReceive(end);
                return;
            }
            if (!(data is DataInternalBlob))
                data.Meta = data.GenerateMeta(Data);
            if (!data.FilterSend(Data))
                return;
            if (!IsAlive)
                return;
            lock (SendFilterLock)
                if (!(_OnSendFilter?.InvokeWhileTrue(this, data) ?? true))
                    return;
            GetQueue(data)?.Enqueue(data);
        }

        public virtual CelesteNetSendQueue GetQueue(DataType data) {
            return DefaultSendQueue;
        }

        public abstract void SendRaw(CelesteNetSendQueue queue, DataType data);

        public virtual void SendRawFlush() {
        }

        protected virtual void Receive(DataType data) {
            if (data is DataLowLevelStringMapping mapping) {
                DefaultSendQueue.Strings.RegisterWrite(mapping.Value, mapping.ID);
                return;
            }

            lock (ReceiveFilterLock)
                if (!(_OnReceiveFilter?.InvokeWhileTrue(this, data) ?? true))
                    return;
            Data.Handle(this, data);
        }

        protected virtual void LoopbackReceive(DataInternalLoopbackMessage msg) {
            Receive(msg);
        }

        protected virtual void LoopbackReceive(DataInternalLoopend end) {
            end.Action();
        }

        public virtual void LogCreator(LogLevel level) {
            Logger.Log(level, "con", $"Creator: {Creator}");
        }

        protected virtual void Dispose(bool disposing) {
            IsAlive = false;
            foreach (CelesteNetSendQueue queue in SendQueues)
                queue.Dispose();
            _OnDisconnect?.Invoke(this);
        }

        public void Dispose() {
            lock (DisposeLock) {
                if (!IsAlive)
                    return;
                Dispose(true);
            }
        }

    }

    public class CelesteNetSendQueue : IDisposable {

        public readonly CelesteNetConnection Con;

        public readonly StringMap Strings;

        private readonly object QueueLock = new();
        private DataType?[] Queue = new DataType?[256];
        private int QueueSendNext = 0;
        private int QueueAddNext = 0;
        private int QueueCount = 0;
        private readonly ManualResetEvent Event;
        private readonly WaitHandle[] EventHandles;
        private readonly Thread Thread;
        private readonly Dictionary<string, Dictionary<uint, DataDedupe>> LastSent = new();
        private readonly List<DataDedupe> Dedupes = new();

        private ulong DedupeTimestamp;

        private DateTime LastUpdate;
        private DateTime LastNonUpdate;

        public readonly BufferHelper Buffer;

        public bool SendKeepAliveUpdate;
        public bool SendKeepAliveNonUpdate;

        public bool SendStringMapUpdate;

        public int MaxCount = 0;

        public CelesteNetSendQueue(CelesteNetConnection con, string name) {
            Con = con;

            Strings = new(name);

            Buffer = new(con.Data, Strings);

            Event = new(false);
            EventHandles = new WaitHandle[] { Event };

            Thread = new(ThreadLoop) {
                Name = $"{GetType().Name} #{GetHashCode()} for {con}",
                IsBackground = true
            };
            Thread.Start();
        }

        public void Enqueue(DataType data) {
            lock (QueueLock) {
                int count;
                if (MaxCount > 0 && Interlocked.CompareExchange(ref QueueCount, 0, 0) == MaxCount) {
                    QueueSendNext = (QueueSendNext + 1) % Queue.Length;

                } else if ((count = Interlocked.Increment(ref QueueCount)) > Queue.Length) {
                    count--;
                    int next = QueueSendNext;
                    DataType?[] old = Queue;
                    DataType?[] resized = new DataType?[old.Length * 2];
                    if (next + count <= old.Length) {
                        Array.Copy(old, next, resized, 0, count);
                    } else {
                        Array.Copy(old, next, resized, 0, old.Length - next);
                        Array.Copy(old, 0, resized, old.Length - next, count - next);
                    }
                    Queue = resized;
                    QueueSendNext = 0;
                    QueueAddNext = count;
                }

                Queue[QueueAddNext] = data;
                QueueAddNext = (QueueAddNext + 1) % Queue.Length;

                try {
                    Event.Set();
                } catch (ObjectDisposedException) {
                }
            }
        }

        protected virtual void ThreadLoop() {
            try {
                while (Con.IsAlive) {
                    DedupeTimestamp++;

                    int waited = 0;
                    if (Interlocked.CompareExchange(ref QueueCount, 0, 0) == 0)
                        waited = WaitHandle.WaitAny(EventHandles, 1000);

                    if ((waited == WaitHandle.WaitTimeout || DedupeTimestamp % 10 == 0) && LastSent.Count > 0) {
                        for (int i = Dedupes.Count - 1; i >= 0; --i) {
                            DataDedupe slot = Dedupes[i];
                            if (!slot.Update(DedupeTimestamp)) {
                                Dedupes.RemoveAt(i);
                                if (LastSent.TryGetValue(slot.Type, out Dictionary<uint, DataDedupe>? slotByID)) {
                                    slotByID.Remove(slot.ID);
                                    if (slotByID.Count == 0) {
                                        LastSent.Remove(slot.Type);
                                    }
                                }
                            }
                        }
                    }

                    if (!Con.IsAlive)
                        return;

                    DateTime now = DateTime.UtcNow;

                    while (Interlocked.CompareExchange(ref QueueCount, 0, 0) != 0) {
                        DataType? data;
                        lock (QueueLock) {
                            int next = QueueSendNext;
                            data = Queue[next];
                            Queue[next] = null;
                            QueueSendNext = (next + 1) % Queue.Length;
                            Interlocked.Decrement(ref QueueCount);
                        }

                        if (data == null)
                            continue;

                        if (data is DataInternalDisconnect) {
                            Con.Dispose();
                            return;
                        }

                        if ((data.DataFlags & DataFlags.OnlyLatest) == DataFlags.OnlyLatest) {
                            string type = data.GetTypeID(Con.Data);
                            uint id = data.GetDuplicateFilterID();

                            lock (QueueLock) {
                                int next = QueueSendNext;
                                int count = Interlocked.CompareExchange(ref QueueCount, 0, 0);
                                int length = Queue.Length;
                                for (int ii = 0; ii < count; ii++) {
                                    int i = (next + ii) % length;
                                    DataType? d = Queue[i];
                                    if (d != null && d.GetTypeID(Con.Data) == type && d.GetDuplicateFilterID() == id) {
                                        data = d;
                                        Queue[i] = null;
                                    }
                                }
                            }
                        }

                        if ((data.DataFlags & DataFlags.SkipDuplicate) == DataFlags.SkipDuplicate) {
                            string type = data.GetTypeID(Con.Data);
                            uint id = data.GetDuplicateFilterID();

                            if (!LastSent.TryGetValue(type, out Dictionary<uint, DataDedupe>? slotByID))
                                LastSent[type] = slotByID = new();

                            if (slotByID.TryGetValue(id, out DataDedupe? slot)) {
                                if (slot.Data.ConsideredDuplicate(data))
                                    continue;
                                slot.Data = data;
                                slot.Timestamp = DedupeTimestamp;
                                slot.Iterations = 0;
                            } else {
                                Dedupes.Add(slotByID[id] = new(type, id, data, DedupeTimestamp));
                            }

                        }

                        Con.SendRaw(this, data);

                        if ((data.DataFlags & DataFlags.Update) == DataFlags.Update)
                            LastUpdate = now;
                        else
                            LastNonUpdate = now;
                    }

                    if (Con.SendStringMap) {
                        List<Tuple<string, int>> added = Strings.PromoteRead();
                        if (added.Count > 0) {
                            foreach (Tuple<string, int> mapping in added)
                                Con.SendRaw(this, new DataLowLevelStringMapping {
                                    IsUpdate = SendStringMapUpdate,
                                    StringMap = Strings.Name,
                                    Value = mapping.Item1,
                                    ID = mapping.Item2
                                });
                            if (SendStringMapUpdate)
                                LastUpdate = now;
                            else
                                LastNonUpdate = now;
                        }
                    }

                    if (Con.SendKeepAlive) {
                        if (SendKeepAliveUpdate && (now - LastUpdate).TotalSeconds >= 1D) {
                            Con.SendRaw(this, new DataLowLevelKeepAlive {
                                IsUpdate = true
                            });
                            LastUpdate = now;
                        }
                        if (SendKeepAliveNonUpdate && (now - LastNonUpdate).TotalSeconds >= 1D) {
                            Con.SendRaw(this, new DataLowLevelKeepAlive {
                                IsUpdate = false
                            });
                            LastNonUpdate = now;
                        }
                    }

                    Con.SendRawFlush();

                    lock (QueueLock)
                        if (Interlocked.CompareExchange(ref QueueCount, 0, 0) == 0)
                            Event.Reset();
                }

            } catch (ThreadInterruptedException) {

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                if (!(e is IOException) && !(e is ObjectDisposedException))
                    Logger.Log(LogLevel.CRI, "conqueue", $"Failed sending data:\n{e}");

                Con.Dispose();

            } finally {
                Event.Dispose();
            }
        }

        protected virtual void Dispose(bool disposing) {
            Buffer.Dispose();

            try {
                Event.Set();
            } catch (ObjectDisposedException) {
            }
        }

        public void Dispose() {
            Dispose(true);
        }

    }

    public class DataDedupe {

        public readonly string Type;
        public readonly uint ID;
        public DataType Data;
        public ulong Timestamp;
        public int Iterations;

        public DataDedupe(string type, uint id, DataType data, ulong timestamp) {
            Type = type;
            ID = id;
            Data = data;
            Timestamp = timestamp;
        }

        public bool Update(ulong timestamp) {
            if (Timestamp + 100 < timestamp)
                Iterations++;
            return Iterations < 3;
        }

    }

    public class BufferHelper : IDisposable {

        public MemoryStream Stream;
        public CelesteNetBinaryWriter Writer;

        public BufferHelper(DataContext ctx, StringMap strings) {
            Stream = new();
            Writer = new(ctx, strings, Stream);
        }

        public void Dispose() {
            Writer?.Dispose();
            Stream?.Dispose();
        }

    }
}
