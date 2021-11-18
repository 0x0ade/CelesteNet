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

        public override string ToString() => $"{GetType()}({ID})";

    }

    public class CelesteNetSendQueue : IDisposable {

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
            timer.Dispose();
        }

        public void Enqueue(DataType data) {
            if (frontQueue.Count >= MaxSize)
                throw new InvalidOperationException("Queue is at maximum size");
            frontQueue.Enqueue(data);
            Flush();
        }

        public void Flush() {
            if (Interlocked.CompareExchange(ref inMergeWindow, 1, 0) == 0)
                timer.Start();
        }

        public void DelayFlush(float delay) {
            if (inMergeWindow != 1)
                throw new InvalidOperationException("Not currently flushing the queue");

            timer.Interval = delay;
            timer.Start();
        }

        public void SignalFlushed() {
            if (inMergeWindow != 1)
                throw new InvalidOperationException("Not currently flushing the queue");

            backQueue.Clear();
            inMergeWindow = 0;
            Interlocked.MemoryBarrier();
            if (frontQueue.Count > 0 && Interlocked.CompareExchange(ref inMergeWindow, 1, 0) == 0)
                timer.Start();
        }

        private void TimerElapsed(object? s, EventArgs a) {
            if (inMergeWindow != 1)
                throw new InvalidOperationException("Not currently flushing the queue");
                
            timer.Interval = MergeWindow;
            backQueue = Interlocked.Exchange(ref frontQueue, backQueue);
            try {
                queueFlushCB(this);
            } catch (Exception e) {
                Logger.Log(LogLevel.WRN, "sendqueue", $"Error flushing connection {Con}'s send queue '{Name}': {e}");
                Con.Dispose();
            }
        }

    }
}
