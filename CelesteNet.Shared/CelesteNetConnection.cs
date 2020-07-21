using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    public abstract class CelesteNetConnection : IDisposable {

        public readonly string Creator = "Unknown";
        public readonly DataContext Data;

        protected readonly object DisposeLock = new object();

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

        private readonly object SendFilterLock = new object();
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

        private readonly object ReceiveFilterLock = new object();
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

        public bool SendKeepAlive;

        protected List<CelesteNetSendQueue> SendQueues = new List<CelesteNetSendQueue>();

        public readonly CelesteNetSendQueue DefaultSendQueue;

        public CelesteNetConnection(DataContext data) {
            Data = data;

            StackTrace trace = new StackTrace();
            foreach (StackFrame? frame in trace.GetFrames()) {
                MethodBase? method = frame?.GetMethod();
                if (method == null || method.IsConstructor)
                    continue;

                string? type = method.DeclaringType?.Name;
                Creator = (type == null ? "" : type + "::") + method.Name;
                break;
            }

            SendQueues.Add(DefaultSendQueue = new CelesteNetSendQueue(this) {
                SendKeepAliveUpdate = true,
                SendKeepAliveNonUpdate = true
            });
        }

        public virtual void Send(DataType? data) {
            if (data == null)
                return;
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

        protected virtual void Receive(DataType data) {
            lock (ReceiveFilterLock)
                if (!(_OnReceiveFilter?.InvokeWhileTrue(this, data) ?? true))
                    return;
            Data.Handle(this, data);
        }

        public virtual void LogCreator(LogLevel level) {
            Logger.Log(level, "con", $"Creator: {Creator}");
        }

        protected virtual void Dispose(bool disposing) {
            IsAlive = false;
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

        private readonly Queue<DataType> Queue = new Queue<DataType>();
        private readonly ManualResetEvent Event;
        private readonly WaitHandle[] EventHandles;
        private readonly Thread Thread;
        // FIXME: MEMORY LEAK! Totally not gonna blame Cruor on this tho, as the initial impl was good for its use case.
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<uint, DataType>> LastSent = new ConcurrentDictionary<string, ConcurrentDictionary<uint, DataType>>();

        private DateTime LastUpdate;
        private DateTime LastNonUpdate;

        public readonly BufferHelper Buffer = new BufferHelper();

        public bool SendKeepAliveUpdate;
        public bool SendKeepAliveNonUpdate;

        public int MaxCount = 0;

        public CelesteNetSendQueue(CelesteNetConnection con) {
            Con = con;

            Event = new ManualResetEvent(false);
            EventHandles = new WaitHandle[] { Event };

            Thread = new Thread(ThreadLoop) {
                Name = $"{GetType().Name} #{GetHashCode()} for {con}",
                IsBackground = true
            };
            Thread.Start();
        }

        public void Enqueue(DataType data) {
            lock (Queue) {
                if (MaxCount > 0 && Queue.Count >= MaxCount)
                    Queue.Dequeue();
                Queue.Enqueue(data);
                try {
                    Event.Set();
                } catch (ObjectDisposedException) {
                }
            }
        }

        protected virtual void ThreadLoop() {
            try {
                while (Con.IsAlive) {
                    if (Queue.Count == 0)
                        WaitHandle.WaitAny(EventHandles, 1000);

                    DateTime now = DateTime.UtcNow;

                    while (Queue.Count > 0) {
                        DataType data;
                        lock (Queue)
                            data = Queue.Dequeue();

                        if (data is DataInternalDisconnect) {
                            Con.Dispose();
                            return;
                        }

                        if ((data.DataFlags & DataFlags.OnlyLatest) == DataFlags.OnlyLatest) {
                            string type = data.GetTypeID(Con.Data);
                            uint id = data.GetDuplicateFilterID();

                            lock (Queue)
                                if (Queue.Where(d => d.GetTypeID(Con.Data) == type && d.GetDuplicateFilterID() == id).Count() > 0)
                                    continue;
                        }

                        if ((data.DataFlags & DataFlags.SkipDuplicate) == DataFlags.SkipDuplicate) {
                            string type = data.GetTypeID(Con.Data);
                            uint id = data.GetDuplicateFilterID();

                            if (!LastSent.TryGetValue(type, out ConcurrentDictionary<uint, DataType>? lastByID))
                                LastSent[type] = lastByID = new ConcurrentDictionary<uint, DataType>();

                            if (lastByID.TryGetValue(id, out DataType? last))
                                if (last.ConsideredDuplicate(data))
                                    continue;

                            lastByID[id] = data;
                        }

                        Con.SendRaw(this, data);

                        if ((data.DataFlags & DataFlags.Update) == DataFlags.Update)
                            LastUpdate = now;
                        else
                            LastNonUpdate = now;
                    }

                    if (Con.SendKeepAlive) {
                        if (SendKeepAliveUpdate && (now - LastUpdate).TotalSeconds >= 1D) {
                            Con.SendRaw(this, new DataKeepAlive {
                                IsUpdate = true
                            });
                            LastUpdate = now;
                        }
                        if (SendKeepAliveNonUpdate && (now - LastNonUpdate).TotalSeconds >= 1D) {
                            Con.SendRaw(this, new DataKeepAlive {
                                IsUpdate = false
                            });
                            LastNonUpdate = now;
                        }
                    }

                    lock (Queue)
                        if (Queue.Count == 0)
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

    public class BufferHelper : IDisposable {

        public MemoryStream Stream;
        public BinaryWriter Writer;

        public BufferHelper() {
            Stream = new MemoryStream();
            Writer = new BinaryWriter(Stream, Encoding.UTF8);
        }

        public void Dispose() {
            Writer?.Dispose();
            Stream?.Dispose();
        }

    }
}
