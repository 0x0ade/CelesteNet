using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public static class QueuedTaskHelper
    {
        private static readonly ConcurrentDictionary<object, object> Map = new ConcurrentDictionary<object, object>();

        private static readonly ConcurrentDictionary<object, Stopwatch> Timers = new ConcurrentDictionary<object, Stopwatch>();

        public static readonly double DefaultDelay = 0.5;

        public static void Cancel(object key)
        {
            if (Timers.TryRemove(key, out var value))
            {
                value.Stop();
                if (!Map.TryRemove(key, out var _))
                {
                    throw new Exception("Queued task cancellation failed!");
                }
            }
        }

        public static Task Do(object key, Action a)
        {
            return Do(key, DefaultDelay, a);
        }

        public static Task Do(object key, double delay, Action a)
        {
            object orAdd = Map.GetOrAdd(key, delegate (object key)
            {
                Stopwatch timer = Stopwatch.StartNew();
                Timers[key] = timer;
                return ((Func<Task>)async delegate
                {
                    do
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delay - timer.Elapsed.TotalSeconds));
                    }
                    while (timer.Elapsed.TotalSeconds < delay);
                    if (timer.IsRunning)
                    {
                        Cancel(key);
                        a?.Invoke();
                    }
                })();
            });
            Timers[key].Restart();
            return (Task)orAdd;
        }

        public static Task<T> Get<T>(object key, Func<T> f)
        {
            return Get(key, DefaultDelay, f);
        }

        public static Task<T> Get<T>(object key, double delay, Func<T> f)
        {
            object orAdd = Map.GetOrAdd(key, delegate (object key)
            {
                Stopwatch timer = Stopwatch.StartNew();
                Timers[key] = timer;
                return ((Func<Task<T>>)async delegate
                {
                    do
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delay - timer.Elapsed.TotalSeconds));
                    }
                    while (timer.Elapsed.TotalSeconds < delay);
                    Cancel(key);
                    return f != null ? f() : default;
                })();
            });
            Timers[key].Restart();
            return (Task<T>)orAdd;
        }
    }
    public static class InvokeHelpers {
        //
        // Summary:
        //     Invokes all delegates in the invocation list, as long as the previously invoked
        //     delegate returns true.
        public static bool InvokeWhileTrue(this MulticastDelegate md, params object[] args) {
            if (md == null) {
                return true;
            }

            Delegate[] invocationList = md.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++) {
                if (!(bool)invocationList[i].DynamicInvoke(args)) {
                    return false;
                }
            }

            return true;
        }
    }
}