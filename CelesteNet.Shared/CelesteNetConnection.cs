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

        private object DisposeLock = new object();
        private Action<CelesteNetConnection> _OnDisconnect;
        public event Action<CelesteNetConnection> OnDisconnect {
            add {
                lock (DisposeLock) {
                    _OnDisconnect += value;
                    if (!IsAlive)
                        value?.Invoke(this);
                }
            }
            remove {
                _OnDisconnect -= value;
            }
        }

        private readonly Queue<DataType> SendQueue = new Queue<DataType>();
        private readonly ManualResetEvent SendQueueEvent;
        private readonly WaitHandle[] SendQueueEventHandles;
        private readonly Thread SendQueueThread;

        public virtual bool IsAlive { get; protected set; } = true;
        public abstract bool IsConnected { get; }
        public abstract string ID { get; }

        public bool SendKeepAlive;

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

            SendQueueThread = new Thread(SendQueueThreadLoop) {
                Name = $"{GetType().Name} SendQueue ({Creator} - {GetHashCode()})",
                IsBackground = true
            };
            SendQueueThread.Start();
        }

        public void Send(DataType data) {
            lock (SendQueue) {
                SendQueue.Enqueue(data);
                SendQueueEvent.Set();
            }
        }

        public abstract void SendRaw(DataType data);

        protected virtual void Receive(DataType data) {
            Data.Handle(this, data);
        }

        public virtual void LogCreator(LogLevel level) {
            Logger.Log(level, "con", $"Creator: {Creator}");
        }

        protected virtual void SendQueueThreadLoop() {
            try {
                while (IsAlive) {
                    if (SendQueue.Count == 0)
                        WaitHandle.WaitAny(SendQueueEventHandles, 1000);

                    if (SendQueue.Count == 0 && SendKeepAlive) {
                        SendRaw(new DataKeepAlive {
                            IsUpdate = false
                        });
                        SendRaw(new DataKeepAlive {
                            IsUpdate = true
                        });
                        continue;
                    }

                    while (SendQueue.Count > 0) {
                        DataType data;
                        lock (SendQueue)
                            data = SendQueue.Dequeue();
                        SendRaw(data);
                    }

                    lock (SendQueue)
                        if (SendQueue.Count == 0)
                            SendQueueEvent.Reset();
                }

            } catch (ThreadInterruptedException) {

            } catch (ThreadAbortException) {

            } catch (Exception e) {
                if (!(e is IOException) && !(e is ObjectDisposedException))
                    Logger.Log(LogLevel.CRI, "con", $"Failed sending data:\n{e}");

                Dispose();

            } finally {
                SendQueueEvent.Dispose();
            }
        }

        protected virtual void Dispose(bool disposing) {
            IsAlive = false;
            _OnDisconnect?.Invoke(this);
            try {
                SendQueueEvent.Set();
            } catch (ObjectDisposedException) {
            }
        }

        public void Dispose() {
            lock (DisposeLock) {
                if (!IsAlive)
                    return;
                Dispose(true);
            }
        }

    }
}
