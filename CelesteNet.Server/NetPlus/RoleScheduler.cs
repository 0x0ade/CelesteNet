using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

            public NetPlusThreadRole Role;
            public float ActvRate;
            public int NumThreads;
            public int NumStolenThreads;

            public RoleMetadata(NetPlusThreadRole role) {
                Role = role;
                ActvRate = 0;
                NumThreads = 0;
                NumStolenThreads = 0;
            }

        }

        private readonly RWLock _RoleLock = new();
        private readonly List<NetPlusThreadRole> Roles = new();

        private Timer? SchedulerTimer;
        private readonly object SchedulerLock = new();
        private long _LastSchedulerExecDuration;
        private int _LastSchedulerExecNumThreadsReassigned, _LastSchedulerExecNumThreadsIdeling;

        internal NetPlusRoleScheduler(NetPlusThreadPool pool, float timerInterval, float underloadThreshold, float overloadThreshold, float stealThreshold) {
            Pool = pool;
            UnderloadThreshold = underloadThreshold;
            OverloadThreshold = overloadThreshold;
            StealThreshold = stealThreshold;

            if (timerInterval > 0) {
                // Create scheduler timer
                SchedulerTimer = new(timerInterval);
                SchedulerTimer.Elapsed += (_, _) => InvokeScheduler();
                SchedulerTimer.Enabled = true;
            }
        }

        public void Dispose() {
            // Stop the scheduler timer
            lock (SchedulerLock) {
                if (SchedulerTimer != null) {
                    SchedulerTimer.Dispose();
                    SchedulerTimer = null;
                }
            }

            // Dispose roles
            foreach (NetPlusThreadRole role in Roles)
                role.Dispose();
            Roles.Clear();
            _RoleLock.Dispose();
        }

        public void InvokeScheduler() {
            OnPreScheduling?.Invoke();
            lock (SchedulerLock) {
                using (Pool.PoolLock.W())
                using (Pool.RoleLock.W())
                using (_RoleLock.R()) {
                    Stopwatch watch = new();
                    Logger.Log(LogLevel.DBG, "netplus", "Invoking thread pool scheduler...");

                    // Collect metadata from roles and threads
                    List<(NetPlusThreadRole role, RoleMetadata metadata)> roles = new();
                    Dictionary<NetPlusThreadRole, int> roleIdxs = new();
                    foreach (NetPlusThreadRole role in Roles) {
                        roles.Add((role, new(role)));
                        roleIdxs.Add(role, roles.Count-1);
                    }

                    List<(NetPlusThread thread, float actvRate, RoleMetadata? role)> threads = new();
                    foreach (NetPlusThread thread in Pool.EnumerateThreads()) {
                        float actvRate = thread.ActivityRate;
                        RoleMetadata? md = null;
                        if (roleIdxs.TryGetValue(thread.Role, out int roleIdx)) {
                            md = roles[roleIdx].metadata;
                            md.ActvRate += actvRate;
                            md.NumThreads++;
                        }
                        threads.Add((thread, actvRate, md));
                    }

                    foreach ((_, RoleMetadata md) in roles)
                        md.ActvRate /= md.NumThreads;

                    // Sort roles and threads by their activity rates
                    roles.Sort((r1, r2) => r1.metadata.ActvRate.CompareTo(r2.metadata.ActvRate));
                    threads.Sort((t1, t2) => t1.actvRate.CompareTo(t2.actvRate));

                    // Iterate over all overloaded roles or roles with too little threads, and assign them to underloaded threads
                    _LastSchedulerExecNumThreadsReassigned = 0;
                    for (int ri = roles.Count - 1; ri >= 0; ri--) {
                        bool needsThread = (roles[ri].metadata.ActvRate >= OverloadThreshold || roles[ri].metadata.NumThreads < roles[ri].role.MinThreads);
                        if (!needsThread || roles[ri].role.MaxThreads <= roles[ri].metadata.NumThreads)
                            continue;

                        // Find an underloaded thread
                        int threadIdx = -1;
                        for (int ti = 0; ti < threads.Count; ti++) {
                            if (threads[ti].actvRate > UnderloadThreshold)
                                break;

                            // Check the thread's role
                            RoleMetadata? md = threads[ti].role;
                            if (md != null && (md.NumThreads <= md.Role.MinThreads || md.ActvRate >= StealThreshold))
                                continue;

                            // We found an underloaded thread!
                            threadIdx = ti;
                            break;
                        }

                        if (threadIdx < 0)
                            break;

                        // Assign the thread to this role
                        Logger.Log(LogLevel.DBG, "netplus", $"Assigning thread pool thread {threads[threadIdx].thread.Index} to role {roles[ri].role}...");
                        threads[threadIdx].thread.Role = roles[ri].role;

                        roles[ri].metadata.ActvRate = roles[ri].metadata.ActvRate * roles[ri].metadata.NumThreads / (roles[ri].metadata.NumThreads+1);
                        roles[ri].metadata.NumThreads++;

                        RoleMetadata? metadata = threads[threadIdx].role;
                        if (metadata != null) {
                            metadata.ActvRate = metadata.ActvRate * metadata.NumThreads / (metadata.NumThreads-1);
                            metadata.NumThreads--;
                            metadata.NumStolenThreads++;
                        }

                        threads.RemoveAt(threadIdx);
                        _LastSchedulerExecNumThreadsReassigned++;
                    }

                    // Let the idle role steal all remaining underloaded threads
                    _LastSchedulerExecNumThreadsIdeling = 0;
                    for (int ti = 0; ti < threads.Count; ti++) {
                        if (threads[ti].actvRate > UnderloadThreshold)
                            break;

                        // Check the thread's role
                        RoleMetadata? md = threads[ti].role;
                        if (md == null || (md.NumThreads <= md.Role.MinThreads || md.ActvRate >= StealThreshold))
                            continue;

                        Logger.Log(LogLevel.DBG, "netplus", $"Letting thread pool thread {threads[ti].thread.Index} idle...");
                        threads[ti].thread.Role = Pool.IdleRole;

                        md.ActvRate = md.ActvRate * md.NumThreads / (md.NumThreads-1);
                        md.NumThreads--;
                        md.NumStolenThreads++;
                        _LastSchedulerExecNumThreadsIdeling++;
                    }

                    // Invoke the individual role schedulers
                    foreach (NetPlusThreadRole role in Roles)
                        role.InvokeSchedular();

                    // Indicate that threads have been running stable
                    foreach ((NetPlusThread thread, _, _) in threads)
                        Pool.IndicateThreadStable(thread);

                    Logger.Log(LogLevel.DBG, "netplus", $"Thread pool scheduler done in {watch.ElapsedMilliseconds}ms");
                    _LastSchedulerExecDuration = watch.ElapsedMilliseconds;

                    SchedulerTimer?.Start();
                }
            }
            OnPostScheduling?.Invoke();
        }

        public void AddRole(NetPlusThreadRole role) {
            using (_RoleLock.W()) {
                if (Roles.Aggregate(0, (a, r) => a + r.MinThreads) + role.MinThreads > Pool.NumThreads)
                    throw new InvalidOperationException("Maximum thread Roles reached");
                Roles.Add(role);
            }
            InvokeScheduler();
        }

        public IEnumerable<NetPlusThreadRole> EnumerateRoles(){
            using (_RoleLock.R()) {
                foreach(NetPlusThreadRole role in Roles)
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

        public RWLock RoleLock => _RoleLock;

        public NetPlusThreadPool Pool { get; }
        public float UnderloadThreshold { get; }
        public float OverloadThreshold { get; }
        public float StealThreshold { get; }

        public long LastSchedulerExecDuration { get { lock(SchedulerLock) return _LastSchedulerExecDuration; } }
        public int LastSchedulerExecNumThreadsReassigned { get { lock(SchedulerLock) return _LastSchedulerExecNumThreadsReassigned; } }
        public int LastSchedulerExecNumThreadsIdeling { get { lock(SchedulerLock) return _LastSchedulerExecNumThreadsIdeling; } }

        public event Action? OnPreScheduling, OnPostScheduling;

    }
}