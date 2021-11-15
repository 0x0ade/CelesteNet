using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;

namespace Celeste.Mod.CelesteNet.Server {
    /*
    The role scheduler is invoked at a regular interval, and everytime the roles
    change. It's job is it to make sure all roles are balanced, and to
    distribute threads among roles, so that individual threads aren't overloaded
    and aren't inactive
    */
    public class NetPlusRoleScheduler : IDisposable {

        private class RoleMetadata {

            public NetPlusThreadRole role;
            public float actvRate;
            public int numThreads;
            public int numStolenThreads;

            public RoleMetadata(NetPlusThreadRole role) {
                this.role = role;
                this.actvRate = 0;
                this.numThreads = 0;
                this.numStolenThreads = 0;
            }

        }

        private RWLock roleLock;
        private List<NetPlusThreadRole> roles;

        private Timer? schedulerTimer;
        private object schedulerLock = new object();
        private long lastSchedulerExecDuration;
        private int lastSchedulerExecNumThreadsReassigned, lastSchedulerExecNumThreadsIdeling;

        internal NetPlusRoleScheduler(NetPlusThreadPool pool, float timerInterval, float underloadThreshold, float overloadThreshold, float stealThreshold) {
            Pool = pool;
            UnderloadThreshold = underloadThreshold;
            OverloadThreshold = overloadThreshold;
            StealThreshold = stealThreshold;

            // Initialize roles
            roleLock = new RWLock();
            roles = new List<NetPlusThreadRole>();

            if (timerInterval > 0) {
                // Create scheduler timer
                schedulerTimer = new Timer(timerInterval);
                schedulerTimer.Elapsed += (_, _) => InvokeScheduler();
                schedulerTimer.AutoReset = true;
                schedulerTimer.Enabled = true;
            }
        }

        public void Dispose() {
            // Stop the scheduler timer
            lock (schedulerLock) {
                if (schedulerTimer != null) {
                    schedulerTimer.Dispose();
                    schedulerTimer = null;
                }
            }

            // Dispose roles
            roles.Clear();
            roleLock.Dispose();
        }

        public void InvokeScheduler() {
            lock (schedulerLock) {
                using (Pool.PoolLock.R())
                using (Pool.RoleLock.W())
                using (roleLock.R()) {
                    Stopwatch watch = new Stopwatch();
                    Logger.Log(LogLevel.DBG, "netplus", "Invoking thread pool scheduler...");
                    
                    // Collect metadata from roles and threads
                    List<(NetPlusThreadRole role, RoleMetadata metadata)> roles = new List<(NetPlusThreadRole, RoleMetadata)>();
                    Dictionary<NetPlusThreadRole, int> roleIdxs = new Dictionary<NetPlusThreadRole, int>();
                    foreach (NetPlusThreadRole role in this.roles) {
                        roles.Add((role, new RoleMetadata(role)));
                        roleIdxs.Add(role, roles.Count-1);
                    }

                    List<(NetPlusThread thread, float actvRate, RoleMetadata? role)> threads = new List<(NetPlusThread, float, RoleMetadata?)>();
                    foreach (NetPlusThread thread in Pool.EnumerateThreads()) {
                        float actvRate = thread.ActivityRate;
                        RoleMetadata? md = null;
                        if (roleIdxs.TryGetValue(thread.Role, out int roleIdx)) {
                            md = roles[roleIdx].metadata;
                            md.actvRate += actvRate;
                            md.numThreads++;
                        }
                        threads.Add((thread, actvRate, md));
                    }

                    foreach ((_, RoleMetadata md) in roles)
                        md.actvRate /= md.numThreads;

                    // Sort roles and threads by their activity rates
                    roles.Sort((r1, r2) => r1.metadata.actvRate.CompareTo(r2.metadata.actvRate));
                    threads.Sort((t1, t2) => t1.actvRate.CompareTo(t2.actvRate));
                    
                    // Iterate over all overloaded roles or roles with too little threads, and assign them to underloaded threads
                    lastSchedulerExecNumThreadsReassigned = 0;
                    for (int i = roles.Count - 1; i >= 0; i--) {
                        bool needsThread = (roles[i].metadata.actvRate >= OverloadThreshold || roles[i].metadata.numThreads < roles[i].role.MinThreads);
                        if (!needsThread || roles[i].role.MaxThreads <= roles[i].metadata.numThreads)
                            continue;

                        // Find an underloaded thread
                        int threadIdx = -1;
                        for (int j = 0; j < threads.Count; j++) {
                            if (threads[j].actvRate > UnderloadThreshold)
                                break;

                            // Check the thread's role
                            RoleMetadata? md = threads[j].role;
                            if (md != null && (md.numThreads <= md.role.MinThreads || md.actvRate >= StealThreshold))
                                continue;

                            // We found an underloaded thread!
                            threadIdx = j;
                            break;
                        }

                        if (threadIdx < 0)
                            break;

                        // Assign the thread to this role
                        Logger.Log(LogLevel.DBG, "netplus", $"Assigning thread pool thread {threads[threadIdx].thread.Index} to role {roles[i].role}...");
                        threads[threadIdx].thread.Role = roles[i].role;

                        roles[i].metadata.actvRate = roles[i].metadata.actvRate * roles[i].metadata.numThreads / (roles[i].metadata.numThreads+1);
                        roles[i].metadata.numThreads++;

                        RoleMetadata? metadata = threads[threadIdx].role;
                        if (metadata != null) {
                            metadata.actvRate = metadata.actvRate * metadata.numThreads / (metadata.numThreads-1);
                            metadata.numThreads--;
                            metadata.numStolenThreads++;
                        }
                        
                        threads.RemoveAt(threadIdx);
                        lastSchedulerExecNumThreadsReassigned++;
                    }

                    // Let the idle role steal all remaining underloaded threads
                    lastSchedulerExecNumThreadsIdeling = 0;
                    for (int j = 0; j < threads.Count; j++) {
                        if (threads[j].actvRate > UnderloadThreshold)
                            break;

                        // Check the thread's role
                        RoleMetadata? md = threads[j].role;
                        if (md == null || (md.numThreads <= md.role.MinThreads || md.actvRate >= StealThreshold))
                            continue;

                        Logger.Log(LogLevel.DBG, "netplus", $"Letting thread pool thread {threads[j].thread.Index} idle...");
                        threads[j].thread.Role = Pool.IdleRole;

                        md.actvRate = md.actvRate * md.numThreads / (md.numThreads-1);
                        md.numThreads--;
                        md.numStolenThreads++;
                        lastSchedulerExecNumThreadsIdeling++;
                    }

                    // Invoke the individual role schedulers
                    foreach (NetPlusThreadRole role in this.roles)
                        role.InvokeSchedular(); 

                    Logger.Log(LogLevel.DBG, "netplus", $"Thread pool scheduler done in {watch.ElapsedMilliseconds}ms");
                    lastSchedulerExecDuration = watch.ElapsedMilliseconds;
                }
            }
        }

        public void AddRole(NetPlusThreadRole role) {
            using (roleLock.W())
                roles.Add(role);
            InvokeScheduler();
        }

        public IEnumerable<NetPlusThreadRole> EnumerateRoles(){
            using (roleLock.R()) {
                foreach(NetPlusThreadRole role in roles)
                    yield return role;
            }
        }

        public T? FindRole<T>(Func<T, bool>? filter = null) where T : NetPlusThreadRole {
            foreach (NetPlusThreadRole role in EnumerateRoles()) {
                if (role is T t && (filter == null || filter(t)))
                    return t;
            }
            return null;
        }

        public RWLock RoleLock => roleLock;

        public NetPlusThreadPool Pool { get; }
        public float UnderloadThreshold { get; }
        public float OverloadThreshold { get; }
        public float StealThreshold { get; }

        public long LastSchedulerExecDuration { get { lock(schedulerLock) return lastSchedulerExecDuration; } }
        public int LastSchedulerExecNumThreadsReassigned { get { lock(schedulerLock) return lastSchedulerExecNumThreadsReassigned; } }
        public int LastSchedulerExecNumThreadsIdeling { get { lock(schedulerLock) return lastSchedulerExecNumThreadsIdeling; } }
    }
}