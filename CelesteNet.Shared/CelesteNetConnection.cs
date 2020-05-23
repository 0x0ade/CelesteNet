using System;
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

        public readonly string Creator;
        public readonly DataContext Data;

        public event Action<CelesteNetConnection, DataType> OnReceive;
        public event Action<CelesteNetConnection> OnDisconnect;

        private readonly Queue<DataType> SendQueue = new Queue<DataType>();
        private readonly ManualResetEvent SendQueueEvent;
        private readonly WaitHandle[] SendQueueEventHandles;
        private readonly Thread SendQueueThread;

        public bool IsConnected { get; protected set; } = true;

        public CelesteNetConnection(DataContext data) {
            Data = data;

            StackTrace trace = new StackTrace();
            foreach (StackFrame frame in trace.GetFrames()) {
                MethodBase method = frame.GetMethod();
                if (method.IsConstructor)
                    continue;

                Creator = method.DeclaringType?.Name;
                Creator = (Creator == null ? "" : Creator + "::") + method.Name;
                break;
            }

            SendQueueEvent = new ManualResetEvent(false);
            SendQueueEventHandles = new WaitHandle[] { SendQueueEvent };

            SendQueueThread = new Thread(SendQueueThreadLoop);
            SendQueueThread.Name = $"{GetType().Name} SendQueue ({Creator} - {GetHashCode()})";
            SendQueueThread.IsBackground = true;
            SendQueueThread.Start();
        }

        public void SendLazy(DataType data) {
            lock (SendQueue) {
                SendQueue.Enqueue(data);
                SendQueueEvent.Set();
            }
        }

        public abstract void Send(DataType data);

        protected virtual void Receive(DataType data) {
            OnReceive?.Invoke(this, data);
        }

        public virtual void LogCreator(LogLevel level) {
            Logger.Log(level, "con", $"Creator: {Creator}");
        }

        protected virtual void SendQueueThreadLoop() {
            try {
                while (IsConnected) {
                    if (SendQueue.Count == 0)
                        WaitHandle.WaitAny(SendQueueEventHandles);

                    while (SendQueue.Count > 0) {
                        DataType data;
                        lock (SendQueue)
                            data = SendQueue.Dequeue();
                        Send(data);
                    }

                    lock (SendQueue)
                        if (SendQueue.Count == 0)
                            SendQueueEvent.Reset();
                }

            } catch (ThreadAbortException) {
                // Just a normal abort.

            } catch {
                Dispose();
            }
        }

        protected virtual void Dispose(bool disposing) {
            OnDisconnect?.Invoke(this);
            SendQueueThread?.Abort();
        }

        public void Dispose() {
            Dispose(true);
        }

    }
}
