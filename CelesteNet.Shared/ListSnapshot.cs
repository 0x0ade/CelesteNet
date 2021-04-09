using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectFactories;

namespace Celeste.Mod.CelesteNet {
    public class ListSnapshot<T> : IList<T>, IDisposable {

        internal bool Disposed;
        public readonly ListSnapshotPool<T> Pool;
        public readonly List<T> List = new List<T>();

        public ListSnapshot(ListSnapshotPool<T> pool) {
            Pool = pool;
        }

        public void Set(IEnumerable<T> list) {
            List.Clear();
            List.AddRange(list);
        }

        public void Dispose() {
            Pool.Add(this);
        }

        // Auto-generated IList implementation.

        public T this[int index] { get => ((IList<T>) List)[index]; set => ((IList<T>) List)[index] = value; }

        public int Count => ((ICollection<T>) List).Count;

        public bool IsReadOnly => ((ICollection<T>) List).IsReadOnly;

        public void Add(T item) {
            ((ICollection<T>) List).Add(item);
        }

        public void Clear() {
            ((ICollection<T>) List).Clear();
        }

        public bool Contains(T item) {
            return ((ICollection<T>) List).Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex) {
            ((ICollection<T>) List).CopyTo(array, arrayIndex);
        }

        public IEnumerator<T> GetEnumerator() {
            return ((IEnumerable<T>) List).GetEnumerator();
        }

        public int IndexOf(T item) {
            return ((IList<T>) List).IndexOf(item);
        }

        public void Insert(int index, T item) {
            ((IList<T>) List).Insert(index, item);
        }

        public bool Remove(T item) {
            return ((ICollection<T>) List).Remove(item);
        }

        public void RemoveAt(int index) {
            ((IList<T>) List).RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return ((IEnumerable) List).GetEnumerator();
        }

    }

    public class ListSnapshotPool<T> {

        private readonly ConcurrentBag<ListSnapshot<T>> Bag = new ConcurrentBag<ListSnapshot<T>>();
        private uint Count;

        public uint MaxCount;

        public ListSnapshot<T> Get() {
            if (Bag.TryTake(out ListSnapshot<T>? snapshot)) {
                snapshot.Disposed = false;
                snapshot.Clear();
                return snapshot;
            }

            return new ListSnapshot<T>(this);
        }

        public void Add(ListSnapshot<T> snapshot) {
            if (snapshot.Disposed || (MaxCount != 0 && Count >= MaxCount))
                return;
            Count++;
            snapshot.Disposed = true;
            snapshot.Clear();
            Bag.Add(snapshot);
        }

    }

    public static class ListSnapshotStaticPool<T> {

        public static ListSnapshotPool<T> Pool = new ListSnapshotPool<T>();

    }

    public static class ListSnapshotStaticPool {

        public static ListSnapshot<T> ToSnapshot<T>(this IEnumerable<T> list, RWLock rwlock) {
            ListSnapshot<T> snapshot = ListSnapshotStaticPool<T>.Pool.Get();
            using (rwlock.R())
                snapshot.Set(list);
            return snapshot;
        }

        public static ListSnapshot<T> ToSnapshot<T>(this IEnumerable<T> list, object rlock) {
            ListSnapshot<T> snapshot = ListSnapshotStaticPool<T>.Pool.Get();
            if (rlock != null) {
                lock (rlock)
                    snapshot.Set(list);
            } else {
                snapshot.Set(list);
            }
            return snapshot;
        }

        public static ListSnapshot<T> ToSnapshot<T>(this IEnumerable<T> list) {
            ListSnapshot<T> snapshot = ListSnapshotStaticPool<T>.Pool.Get();
            lock (list)
                snapshot.Set(list);
            return snapshot;
        }

    }
}
