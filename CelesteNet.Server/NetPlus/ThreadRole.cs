using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    /*
    A thread role represents something a thread could be doing, e.g. waiting for
    TCP connections, polling sockets, flushing send queues, or just ideling. The
    thread pool contains a list of thread roles, and it will monitor their
    activity rates and heuristicaly distribute roles among all threads. 
    -Popax21
    */
    public abstract class NetPlusThreadRole : IDisposable {

        public abstract class RoleWorker : IDisposable {
            internal Stopwatch runtimeWatch;

            private int heuristicSampleWindow;
            private RWLock activityLock;
            private bool inActiveZone;
            private long lastActivityUpdate = long.MinValue;
            private float lastActivityRate = 0f;

            protected RoleWorker(NetPlusThreadRole role, NetPlusThread thread) {
                Role = role;
                Thread = thread;

                // Init heuristic stuff
                activityLock = new RWLock();
                heuristicSampleWindow = thread.Pool.HeuristicSampleWindow;
                runtimeWatch = new Stopwatch();
            }

            public void Dispose() {
                activityLock.Dispose();
            }

            protected internal abstract void StartWorker(CancellationToken token);

            protected void EnterActiveZone() {
                using (activityLock.W()) {
                    long curMs = runtimeWatch.ElapsedMilliseconds;
                    lastActivityRate = CalcActivityRate(curMs);
                    inActiveZone = true;
                    lastActivityUpdate = curMs;
                }
            }

            protected void ExitActiveZone() {
                using (activityLock.W()) {
                    long curMs = runtimeWatch.ElapsedMilliseconds;
                    lastActivityRate = CalcActivityRate(curMs);
                    inActiveZone = false;
                    lastActivityUpdate = curMs;
                }
            }

            private float CalcActivityRate(long curMs) {
                long cutoffMs = curMs - heuristicSampleWindow;
                float rate = (lastActivityRate < cutoffMs) ? 0 : lastActivityRate * (lastActivityRate - cutoffMs) / heuristicSampleWindow;
                rate += MathF.Min(1, (float) (curMs - lastActivityRate) / heuristicSampleWindow);
                return MathF.Min(1, MathF.Max(0, rate));
            }

            public NetPlusThread Thread { get; }
            public NetPlusThreadRole Role { get; }

            public float ActivityRate {
                get {
                    using (activityLock.R())
                        return CalcActivityRate(runtimeWatch.ElapsedMilliseconds);
                }
            }
        }

        private bool disposed = false;
        private RWLock workerLock;
        private List<RoleWorker> workers;

        protected NetPlusThreadRole(NetPlusThreadPool pool) {
            Pool = pool;

            // Init workers collection
            using ((workerLock = new RWLock()).W())
                workers = new List<RoleWorker>();
        }

        public void Dispose() {
            if(disposed) return;
            disposed = true;

            using (workerLock.W()) {
                workers.Clear();
                workerLock.Dispose();
            }
        }

        public IEnumerable<RoleWorker> EnumerateWorkers() {
            using (workerLock.R()) {
                foreach(RoleWorker worker in workers)
                    yield return worker;
            }
        }

        public RoleWorker? FindThread(Func<RoleWorker, bool> filter){
            foreach (RoleWorker worker in EnumerateWorkers()) {
                if (filter(worker))
                    return worker;
            }
            return null;
        }

        private void RegisterWorker(RoleWorker worker) {}
        private void UnregisterWorker(RoleWorker worker) {}

        public abstract RoleWorker CreateWorker(NetPlusThread thread);

        public NetPlusThreadPool Pool { get; }
        public float ActivityRate => EnumerateWorkers().Aggregate(0f, (a, w) => a + w.ActivityRate) / workers.Count;

    }

    public sealed class IdleThreadRole : NetPlusThreadRole {

        private sealed class Worker : RoleWorker {
            public Worker(NetPlusThreadRole role, NetPlusThread thread) : base(role, thread) {}

            protected internal override void StartWorker(CancellationToken token) => token.WaitHandle.WaitOne();
        }

        public IdleThreadRole(NetPlusThreadPool pool) : base(pool) {}

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

    }
}