using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server
{
    /*
    A thread role represents something a thread could be doing, e.g. waiting for
    TCP connections, polling sockets, flushing send queues, or just ideling. The
    thread pool contains a list of thread roles, and it will monitor their
    activity rates and heuristicaly distribute roles among all threads.
    -Popax21
    */
    public abstract class NetPlusThreadRole : IDisposable {

        public abstract class RoleWorker : IDisposable {
            private readonly RWLock ActivityLock;
            internal int ActiveZoneCounter = 0;
            private long LastActivityUpdate = long.MinValue;
            private float LastActivityRate = 0f;

            protected RoleWorker(NetPlusThreadRole role, NetPlusThread thread) {
                Role = role;
                Thread = thread;

                // Init heuristic stuff
                ActivityLock = new();

                using (Role.WorkerLock.W())
                    role.Workers.Add(this);
            }

            public virtual void Dispose() {
                if (Role.Workers.Contains(this))
                    using (Role.WorkerLock.W())
                        Role.Workers.Remove(this);
                ActivityLock.Dispose();
            }

            protected internal abstract void StartWorker(CancellationToken token);

            protected void EnterActiveZone() {
                using (ActivityLock.W())
                    Thread.Pool.IterateSteadyHeuristic(ref LastActivityRate, ref LastActivityUpdate, (ActiveZoneCounter++ > 0) ? 1f : 0f, true);
            }

            protected void ExitActiveZone() {
                using (ActivityLock.W()) {
                    if (ActiveZoneCounter <= 0)
                        throw new InvalidOperationException("Not in an active zone");
                    Thread.Pool.IterateSteadyHeuristic(ref LastActivityRate, ref LastActivityUpdate, (ActiveZoneCounter-- > 0) ? 1f : 0f, true);
                }
            }

            public NetPlusThread Thread { get; }
            public NetPlusThreadRole Role { get; }

            public float ActivityRate {
                get {
                    using (ActivityLock.R())
                        return Thread.Pool.IterateSteadyHeuristic(ref LastActivityRate, ref LastActivityUpdate, (ActiveZoneCounter > 0) ? 1f : 0f);
                }
            }
        }

        public NetPlusThreadPool Pool { get; }
        public float ActivityRate => EnumerateWorkers().Aggregate(0f, (a, w) => a + w.ActivityRate / Workers.Count);

        public abstract int MinThreads { get; }
        public abstract int MaxThreads { get; }

        private bool Disposed = false;
        private readonly RWLock WorkerLock = new();
        private readonly List<RoleWorker> Workers = new();

        protected NetPlusThreadRole(NetPlusThreadPool pool) {
            Pool = pool;
        }

        public virtual void Dispose() {
            if (Disposed)
                return;
            Disposed = true;

            using (WorkerLock.W()) {
                Workers.Clear();
            }
            WorkerLock.Dispose();
        }

        public virtual void InvokeSchedular() {}

        public IEnumerable<RoleWorker> EnumerateWorkers() {
            using (WorkerLock.R())
            foreach (RoleWorker worker in Workers)
                yield return worker;
        }

        public RoleWorker? FindWorker(Func<RoleWorker, bool> filter){
            foreach (RoleWorker worker in EnumerateWorkers()) {
                if (filter(worker))
                    return worker;
            }
            return null;
        }

        public abstract RoleWorker CreateWorker(NetPlusThread thread);

    }

    public sealed class IdleThreadRole : NetPlusThreadRole {

        private sealed class Worker : RoleWorker {
            public Worker(IdleThreadRole role, NetPlusThread thread) : base(role, thread) {}

            protected internal override void StartWorker(CancellationToken token) => token.WaitHandle.WaitOne();
        }

        public override int MinThreads => 0;
        public override int MaxThreads => int.MaxValue;

        public IdleThreadRole(NetPlusThreadPool pool) : base(pool) {}

        public override NetPlusThreadRole.RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);

    }
}