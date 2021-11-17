using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Celeste.Mod.CelesteNet.Server {
    public class NetPlusThreadPool : IDisposable {
        private bool disposed = false;
        private RWLock poolLock;
        private Stopwatch runtimeWatch;
        
        private NetPlusThread[] threads;
        private int[] threadRestarts;
        private CancellationTokenSource tokenSrc;
        
        private RWLock roleLock;
        private IdleThreadRole idleRole;

        private NetPlusRoleScheduler scheduler;

        public NetPlusThreadPool(int numThreads, int maxThreadRestarts, int heuristicSampleWindow, float schedulerInterval, float underloadThreshold, float overloadThreshold, float stealThreshold) {
            MaxThreadsRestart = maxThreadRestarts;
            HeuristicSampleWindow = heuristicSampleWindow;

            // Start the runtime watch
            runtimeWatch = new Stopwatch();
            runtimeWatch.Start();

            // Create locks
            using ((poolLock = new RWLock()).W())
            using ((roleLock = new RWLock()).W()) {
                // Create threads
                idleRole = new IdleThreadRole(this);
                tokenSrc = new CancellationTokenSource();
                Logger.Log(LogLevel.INF, "netplus", $"Creating thread pool with {numThreads} threads");
                threadRestarts = new int[numThreads];
                threads = Enumerable.Range(0, numThreads).Select(idx => new NetPlusThread(this, idx, idleRole)).ToArray();
            }

            // Create the schedular
            scheduler = new NetPlusRoleScheduler(this, schedulerInterval, underloadThreshold, overloadThreshold, stealThreshold);
        }

        public void Dispose() {
            if (disposed)
                return;
            disposed = true;

            // Stop the scheduler
            scheduler.Dispose();

            using (poolLock.W())
            using (roleLock.W()) {
                // Stop threads
                tokenSrc.Dispose();
                foreach (NetPlusThread thread in threads)
                    thread.Thread.Join();
                
                runtimeWatch.Stop();
                roleLock.Dispose();
                poolLock.Dispose();
            }
        }

        public float IterateSteadyHeuristic(ref float lastVal, ref long lastUpdate, float curVal, bool update=false) {
            long curMs = runtimeWatch.ElapsedMilliseconds;
            long cutoffMs = curMs - HeuristicSampleWindow;
            float newVal = (lastUpdate < cutoffMs) ? curVal : ((lastVal * (lastUpdate - cutoffMs) + curVal * (curMs - lastUpdate)) / HeuristicSampleWindow);
            if (update) {
                lastVal = newVal;
                lastUpdate = curMs;
            }
            return newVal;
        }

        public float IterateEventHeuristic(ref float lastVal, ref long lastUpdate, int numEvents, bool update=false) {
            long curMs = runtimeWatch.ElapsedMilliseconds;
            long cutoffMs = curMs - HeuristicSampleWindow;
            float newVal = (lastUpdate < cutoffMs) ? ((float) numEvents / (curMs - lastUpdate) * HeuristicSampleWindow) : ((lastVal * (lastUpdate - cutoffMs) + numEvents) / HeuristicSampleWindow);
            if (update) {
                lastVal = newVal;
                lastUpdate = curMs;
            }
            return newVal;
        }

        public IEnumerable<NetPlusThread> EnumerateThreads() {
            using (poolLock.R())
            foreach (NetPlusThread thread in threads)
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
            if (!disposed) {
                if (++threadRestarts[thread.Index] < MaxThreadsRestart) {
                    using (poolLock.W()) {
                        Logger.Log(LogLevel.DBG, "netplus", $"Restarting thread pool thread {thread.Index}");
                        threads[thread.Index] = new NetPlusThread(this, thread.Index, thread.Role);
                    }
                } else throw new InvalidOperationException($"Too many restarts for thread pool thread {thread.Index}", ex);
            }
        }

        public RWLock PoolLock => poolLock;
        public RWLock RoleLock => roleLock;

        public int NumThreads => threads.Length;
        public int MaxThreadsRestart { get; }
        public IdleThreadRole IdleRole => idleRole;
        public NetPlusRoleScheduler Scheduler => scheduler;
        public CancellationToken Token => tokenSrc.Token;

        public int HeuristicSampleWindow { get; }
        public float ActivityRate => EnumerateThreads().Aggregate(0f, (a, t) => a + t.ActivityRate) / threads.Length;
    }

    public class NetPlusThread {
        private Thread thread;
        
        private NetPlusThreadRole role;
        private SemaphoreSlim roleSwitchSem;
        private NetPlusThreadRole.RoleWorker? roleWorker;
        private CancellationTokenSource? roleWorkerTokenSrc;
        private float lastActivityRate = 0f;

        internal NetPlusThread(NetPlusThreadPool pool, int idx, NetPlusThreadRole initRole) {
            Pool = pool;
            Index = idx;
            role = initRole;
            roleSwitchSem = new SemaphoreSlim(0, 1);

            // Start the thread
            thread = new Thread(ThreadLoop);
            thread.Name = $"Thread Pool {pool} Thread {idx}";
            thread.Start();
        }

        private void ThreadLoop() {
            CancellationToken? lastWorkerToken = null;
            try {
                CancellationToken poolToken = Pool.Token;
                poolToken.Register(() => roleWorkerTokenSrc?.Cancel());
                while (!poolToken.IsCancellationRequested) {
                    Logger.Log(LogLevel.DBG, "netplus", $"Thread pool thread {Index} starting role worker for role {role}");
                    
                    // Start the worker
                    using (roleWorker = role.CreateWorker(this))
                    using (roleWorkerTokenSrc = new CancellationTokenSource()) {
                        lastWorkerToken = roleWorkerTokenSrc.Token;
                        roleSwitchSem.Release();
                        try {
                            roleWorker.StartWorker(roleWorkerTokenSrc.Token);
                        } finally {
                            roleWorker.activeZoneCounter = 0;
                            roleWorker = null;
                            roleWorkerTokenSrc = null;
                        }
                    }
                }
            } catch (Exception e) {
                if (e is OperationCanceledException ce && ce.CancellationToken == lastWorkerToken) return;
                
                // Report error to the pool
                Pool.ReportThreadError(this, e);
            } finally {
                // Dispose stuff
                roleSwitchSem.Dispose();
            }
        }

        public NetPlusThreadPool Pool { get; }
        public int Index { get; }
        public Thread Thread => thread;
        public NetPlusThreadRole Role {
            get => role;
            set {
                if (role == value)
                    return;
                roleSwitchSem.Wait();
                using (Pool.RoleLock.R()) {
                    role = value;
                    roleWorkerTokenSrc?.Cancel();
                }
            }
        }
        public NetPlusThreadRole.RoleWorker? RoleWorker => roleWorker;
        public float ActivityRate => lastActivityRate = roleWorker?.ActivityRate ?? lastActivityRate;
    }
}