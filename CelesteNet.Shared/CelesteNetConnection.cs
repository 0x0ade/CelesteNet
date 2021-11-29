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

        protected abstract CelesteNetSendQueue? GetQueue(DataType data);

        protected virtual void Receive(DataType data) {
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
            _OnDisconnect?.Invoke(this);
        }

        public void Dispose() {
            lock (DisposeLock) {
                if (!IsAlive)
                    return;
                Dispose(true);
            }
        }

        public virtual void DisposeSafe() => Dispose();

        public override string ToString() => $"{GetType()}({ID})";

    }

    public class CelesteNetSendQueue : IDisposable {

        private volatile bool _Alive = true;
        public bool Alive => _Alive;

        public readonly CelesteNetConnection Con;
        public readonly string Name;
        public readonly int MaxSize;
        public readonly float MergeWindow;

        private ConcurrentQueue<DataType> frontQueue, backQueue;
        public ConcurrentQueue<DataType> FrontQueue => frontQueue;
        public ConcurrentQueue<DataType> BackQueue => backQueue;

        private Action<CelesteNetSendQueue> queueFlushCB;
        private int inMergeWindow;
        private System.Timers.Timer timer;
        private volatile bool swapQueues;

        public CelesteNetSendQueue(CelesteNetConnection con, string name, int maxSize, float mergeWindow, Action<CelesteNetSendQueue> queueFlusher) {
            Con = con;
            Name = name;
            MaxSize = maxSize;
            MergeWindow = mergeWindow;

            frontQueue = new ConcurrentQueue<DataType>();
            backQueue = new ConcurrentQueue<DataType>();
            queueFlushCB = queueFlusher;
            inMergeWindow = 0;
            timer = new(mergeWindow);
            timer.AutoReset = false;
            timer.Elapsed += TimerElapsed;
        }

        public void Dispose() {
            lock (timer) {
                _Alive = false;
                timer.Dispose();
            }
        }

        public void Enqueue(DataType data) {
            if (!Alive) return;
            if (frontQueue.Count >= MaxSize) {
                Logger.Log(LogLevel.WRN, "sendqueue", $"Connection {Con}'s send queue '{Name}' is at maximum size");
                Con.DisposeSafe();
                return;
            }
            
            frontQueue.Enqueue(data);
            Flush();
        }

        public void Clear() {
            frontQueue.Clear();
        }

        public void Flush() {
            if (Interlocked.CompareExchange(ref inMergeWindow, 1, 0) == 0) {
                lock (timer) {
                    if (!Alive) return;
                    swapQueues = true;
                    timer.Interval = MergeWindow;
                    timer.Start();
                }
            }
        }

        public void DelayFlush(float delay, bool dropUnreliable) {
            if (Volatile.Read(ref inMergeWindow) != 1)
                throw new InvalidOperationException($"Not currently flushing queue '{Name}'");

            lock (timer) {
                if (dropUnreliable) {
                    ConcurrentQueue<DataType> newBackQueue = new ConcurrentQueue<DataType>();
                    foreach (DataType data in backQueue) {
                        if ((data.DataFlags & DataFlags.Unreliable) == 0)
                            newBackQueue.Enqueue(data);
                    }
                    backQueue = newBackQueue;
                }

                swapQueues = false;
                timer.Interval = delay;
                timer.Start();
            }
        }

        public void SignalFlushed() {
            if (Volatile.Read(ref inMergeWindow) != 1)
                throw new InvalidOperationException($"Not currently flushing queue '{Name}'");

            // TODO For whatever reason, MonoKickstart can't clear concurrent queues
            backQueue = new ConcurrentQueue<DataType>();
            inMergeWindow = 0;
            Interlocked.MemoryBarrier();
            if (frontQueue.Count > 0)
                Flush();
        }

        private void TimerElapsed(object? s, EventArgs a) {
            if (Volatile.Read(ref inMergeWindow) != 1)
                throw new InvalidOperationException($"Not currently flushing queue '{Name}'");

            lock (timer) {
                timer.Stop();
                if (swapQueues)
                    backQueue = Interlocked.Exchange(ref frontQueue, backQueue);
            }

            try {
                queueFlushCB(this);
            } catch (Exception e) {
                Logger.Log(LogLevel.WRN, "sendqueue", $"Error flushing connection {Con}'s send queue '{Name}': {e}");
                Con.DisposeSafe();
            }
        }

    }
}
