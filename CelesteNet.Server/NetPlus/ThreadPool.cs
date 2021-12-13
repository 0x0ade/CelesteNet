using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public class NetPlusThreadPool : IDisposable {
        private bool Disposed = false;
        private RWLock _PoolLock;
        private Stopwatch RuntimeWatch;

        private NetPlusThread[] Threads;
        private int[] ThreadRestarts;
        private CancellationTokenSource TokenSrc;

        private RWLock _RoleLock;
        private IdleThreadRole _IdleRole;
        private NetPlusRoleScheduler _Scheduler;

        public NetPlusThreadPool(int numThreads, int maxThreadRestarts, int heuristicSampleWindow, float schedulerInterval, float underloadThreshold, float overloadThreshold, float stealThreshold) {
            MaxThreadsRestart = maxThreadRestarts;
            HeuristicSampleWindow = heuristicSampleWindow;

            // Start the runtime watch
            RuntimeWatch = new Stopwatch();
            RuntimeWatch.Start();

            // Create locks
            using ((_PoolLock = new RWLock()).W())
            using ((_RoleLock = new RWLock()).W()) {
                // Create threads
                TokenSrc = new CancellationTokenSource();
                _IdleRole = new IdleThreadRole(this);
                Logger.Log(LogLevel.INF, "netplus", $"Creating thread pool with {numThreads} threads");
                ThreadRestarts = new int[numThreads];
                Threads = Enumerable.Range(0, numThreads).Select(idx => new NetPlusThread(this, idx, _IdleRole)).ToArray();
            }

            // Create the schedular
            _Scheduler = new NetPlusRoleScheduler(this, schedulerInterval, underloadThreshold, overloadThreshold, stealThreshold);
        }

        public void Dispose() {
            if (Disposed)
                return;
            Disposed = true;

            // Stop the scheduler
            Scheduler.Dispose();

            using (_PoolLock.W())
            using (_RoleLock.W()) {
                // Stop threads
                TokenSrc.Dispose();
                foreach (NetPlusThread thread in Threads)
                    thread.Thread.Join();

                RuntimeWatch.Stop();
                _RoleLock.Dispose();
                _PoolLock.Dispose();
            }
        }

        public float IterateSteadyHeuristic(ref float lastVal, ref long lastUpdate, float curVal, bool update=false) {
            long curMs = RuntimeWatch.ElapsedMilliseconds;
            long cutoffMs = curMs - HeuristicSampleWindow;
            float newVal = (lastUpdate < cutoffMs) ? curVal : ((lastVal * (lastUpdate - cutoffMs) + curVal * (curMs - lastUpdate)) / HeuristicSampleWindow);
            if (update) {
                lastVal = newVal;
                lastUpdate = curMs;
            }
            return newVal;
        }

        public float IterateEventHeuristic(ref float lastVal, ref long lastUpdate, int numEvents=0, bool update=false) {
            long curMs = RuntimeWatch.ElapsedMilliseconds;
            long cutoffMs = curMs - HeuristicSampleWindow;
            float newVal = (((lastUpdate < cutoffMs) ? 0 : (lastVal * (lastUpdate - cutoffMs) / 1000f)) + numEvents) / HeuristicSampleWindow * 1000f;
            if (update) {
                lastVal = newVal;
                lastUpdate = curMs;
            }
            return newVal;
        }

        public IEnumerable<NetPlusThread> EnumerateThreads() {
            using (PoolLock.R())
            foreach (NetPlusThread thread in Threads)
                yield return thread;
        }

        public NetPlusThread? FindThread(Func<NetPlusThread, bool> filter){
            foreach (NetPlusThread thread in EnumerateThreads()) {
                if (filter(thread))
                    return thread;
            }
            return null;
        }

        internal void ReportThreadError(NetPlusThread thread, Exception ex) {
            // Log the exception
            Logger.Log(LogLevel.CRI, "netplus", $"Error in thread pool thread {thread.Index}: {ex}");

            // It's OK, we can start a new one... right?
            if (!Disposed) {
                using (PoolLock.W()) {
                    if (++ThreadRestarts[thread.Index] < MaxThreadsRestart) {
                            Logger.Log(LogLevel.DBG, "netplus", $"Restarting thread pool thread {thread.Index}");
                            Threads[thread.Index] = new NetPlusThread(this, thread.Index, thread.Role);
                    } else throw new InvalidOperationException($"Too many restarts for thread pool thread {thread.Index}", ex);
                }
            }
        }

        internal void IndicateThreadStable(NetPlusThread thread) {
            // Decrement the thread's restart count
            if (!Disposed) {
                using (PoolLock.W()) {
                    if (ThreadRestarts[thread.Index] > 0)
                        ThreadRestarts[thread.Index]--;
                }
            }
        }

        public RWLock PoolLock => _PoolLock;
        public RWLock RoleLock => _RoleLock;

        public int NumThreads => Threads.Length;
        public int MaxThreadsRestart { get; }
        public IdleThreadRole IdleRole => _IdleRole;
        public NetPlusRoleScheduler Scheduler => _Scheduler;
        public CancellationToken Token => TokenSrc.Token;

        public int HeuristicSampleWindow { get; }
        public float ActivityRate => EnumerateThreads().Aggregate(0f, (a, t) => a + t.ActivityRate) / Threads.Length;
    }

    public class NetPlusThread {
        private Thread _Thread;
        private NetPlusThreadRole _Role;
        private SemaphoreSlim RoleSwitchSem;
        private NetPlusThreadRole.RoleWorker? _RoleWorker;
        private CancellationTokenSource? RoleWorkerTokenSrc;
        private float LastActivityRate = 0f;

        internal NetPlusThread(NetPlusThreadPool pool, int idx, NetPlusThreadRole initRole) {
            Pool = pool;
            Index = idx;
            _Role = initRole;
            RoleSwitchSem = new SemaphoreSlim(0, 1);

            // Start the thread
            _Thread = new Thread(ThreadLoop);
            _Thread.Name = $"CelesteNet.Server Thread Pool {pool} Thread {idx}";
            _Thread.Start();
        }

        private void ThreadLoop() {
            CancellationToken? lastWorkerToken = null;
            try {
                CancellationToken poolToken = Pool.Token;
                poolToken.Register(() => RoleWorkerTokenSrc?.Cancel());
                while (!poolToken.IsCancellationRequested) {
                    Logger.Log(LogLevel.DBG, "netplus", $"Thread pool thread {Index} starting role worker for role {_Role}");

                    // Start the worker
                    using (_RoleWorker = _Role.CreateWorker(this))
                    using (RoleWorkerTokenSrc = new CancellationTokenSource()) {
                        lastWorkerToken = RoleWorkerTokenSrc.Token;
                        RoleSwitchSem.Release();
                        try {
                            while (!RoleWorkerTokenSrc.IsCancellationRequested) {
                                _RoleWorker.StartWorker(RoleWorkerTokenSrc.Token);
                                if (!RoleWorkerTokenSrc.IsCancellationRequested)
                                    Logger.Log(LogLevel.WRN, "netplus", $"Thread pool thread {Index} worker {_RoleWorker} exited prematurely!");
                            }
                        } finally {
                            _RoleWorker.ActiveZoneCounter = 0;
                            _RoleWorker = null;
                            RoleWorkerTokenSrc = null;
                        }
                    }
                }
            } catch (Exception e) {
                if (e is OperationCanceledException ce && ce.CancellationToken == lastWorkerToken) return;

                // Report error to the pool
                Pool.ReportThreadError(this, e);
            } finally {
                // Dispose stuff
                RoleSwitchSem.Dispose();
            }
        }

        public NetPlusThreadPool Pool { get; }
        public int Index { get; }
        public Thread Thread => _Thread;
        public NetPlusThreadRole Role {
            get => _Role;
            set {
                if (_Role == value)
                    return;
                RoleSwitchSem.Wait();
                using (Pool.RoleLock.R()) {
                    _Role = value;
                    RoleWorkerTokenSrc?.Cancel();
                }
            }
        }
        public NetPlusThreadRole.RoleWorker? RoleWorker => _RoleWorker;
        public float ActivityRate => LastActivityRate = _RoleWorker?.ActivityRate ?? LastActivityRate;
    }
}