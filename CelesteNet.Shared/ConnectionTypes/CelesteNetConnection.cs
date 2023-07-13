using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.MonocleCelesteHelpers;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

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
            if (data is not DataInternalBlob)
                data.Meta = data.GenerateMeta(Data);
            if (!data.FilterSend(Data))
                return;
            if (!IsAlive)
                return;
            lock (SendFilterLock)
                if (!(_OnSendFilter?.InvokeWhileTrue(this, data) ?? true))
                    return;
            CelesteNetSendQueue? queue = GetQueue(data);
            if (queue != null)
                queue.Enqueue(data);
            else
                SendNoQueue(data);
        }

        protected abstract CelesteNetSendQueue? GetQueue(DataType data);
        protected virtual void SendNoQueue(DataType data) {
        }

        protected virtual void Receive(DataType data) {
            lock (ReceiveFilterLock)
                if (!(_OnReceiveFilter?.InvokeWhileTrue(this, data) ?? true))
                    return;
            Data.Handle(this, data);
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

        public override string ToString() => $"{GetType()}({ID} [{UID}])";

    }

    public class CelesteNetSendQueue : IDisposable {

        private volatile bool _Alive = true;
        public bool Alive => _Alive && Con.IsConnected;

        public readonly CelesteNetConnection Con;
        public readonly string Name;
        public readonly int MaxSize;
        public readonly float MergeWindow;

        private volatile ConcurrentQueue<DataType> _FrontQueue = new();
        private volatile ConcurrentQueue<DataType> _BackQueue = new();
        public ConcurrentQueue<DataType> FrontQueue => _FrontQueue;
        public ConcurrentQueue<DataType> BackQueue => _BackQueue;

        private volatile bool InMergeWindow = false, FlushingQueue = false;
        private readonly RWLock Lock;
        private readonly Action<CelesteNetSendQueue> QueueFlushCB;
        private readonly System.Timers.Timer Timer;

        public CelesteNetSendQueue(CelesteNetConnection con, string name, int maxSize, float mergeWindow, Action<CelesteNetSendQueue> queueFlusher) {
            Con = con;
            Name = name;
            MaxSize = maxSize;
            MergeWindow = mergeWindow;

            Lock = new();
            QueueFlushCB = queueFlusher;
            Timer = new(mergeWindow);
            Timer.Elapsed += TimerElapsed;
        }

        public void Dispose() {
            if (!_Alive)
                return;
            using (Lock.W()) {
                _Alive = false;
                Timer.Dispose();
            }
            Lock.Dispose();
        }

        public void Enqueue(DataType data) {
            using (Lock.R()) {
                if (!Alive)
                    return;
                if (_FrontQueue.Count >= MaxSize) {
                    Logger.Log(LogLevel.WRN, "sendqueue", $"Connection {Con}'s send queue '{Name}' is at maximum size");
                    if (Logger.Level >= LogLevel.VVV) {
                        try {
                            while (_FrontQueue.TryDequeue(out DataType? packet)) {
                                Logger.Log(LogLevel.VVV, "sendqueue", $"Packet on '{Name}': {packet.GetType()} = {packet}");
                            }
                        } catch { }
                    }
                    Con.DisposeSafe();
                    return;
                }

                _FrontQueue.Enqueue(data);
            }
            Flush();
        }

        public void Flush() {
            if (!Alive || InMergeWindow)
                return;
            using (Lock.W()) {
                if (!Alive || InMergeWindow)
                    return;
                InMergeWindow = true;

                Timer.AutoReset = false;
                Timer.Interval = MergeWindow;
                Timer.Start();
            }
        }

        public void Clear() {
            using (Lock.W()) {
                if (!Alive)
                    return;
                _FrontQueue = new();
            }
        }

        public void DelayFlush(float delay, bool dropUnreliable) {
            using (Lock.W()) {
                if (!Alive || !FlushingQueue)
                    throw new InvalidOperationException($"Not currently flushing queue '{Name}'!");

                if (dropUnreliable) {
                    ConcurrentQueue<DataType> newBackQueue = new();
                    foreach (DataType data in BackQueue) {
                        if ((data.DataFlags & DataFlags.Unreliable) == 0)
                            newBackQueue.Enqueue(data);
                    }
                    _BackQueue = newBackQueue;
                }

                if (delay > 0) {
                    Timer.AutoReset = false;
                    Timer.Interval = delay;
                    Timer.Start();
                } else {
                    Logger.Log(LogLevel.WRN, "sendqueue", $"DelayFlush(): Connection {Con}'s send queue '{Name}' Timer not set because delay was '{delay}'");
                }
            }
        }

        public void SignalFlushed() {
            using (Lock.W()) {
                if (!Alive || !FlushingQueue)
                    throw new InvalidOperationException($"Not currently flushing queue '{Name}'!");

                _BackQueue = new();
                InMergeWindow = FlushingQueue = false;
                if (FrontQueue.Count > 0)
                    Flush();
            }
        }

        private void TimerElapsed(object? s, EventArgs a) {
            using (Lock.W()) {
                if (!Alive || !InMergeWindow)
                    throw new InvalidOperationException($"Not in merge window of queue '{Name}'!");

                Timer.Stop();
                if (!FlushingQueue)
                    _BackQueue = Interlocked.Exchange(ref _FrontQueue, _BackQueue);
                FlushingQueue = true;

                try {
                    QueueFlushCB.Invoke(this);
                } catch (Exception e) {
                    Logger.Log(LogLevel.WRN, "sendqueue", $"Error flushing connection {Con}'s send queue '{Name}': {e}");
                    Con.DisposeSafe();
                }
            }
        }

    }
}
