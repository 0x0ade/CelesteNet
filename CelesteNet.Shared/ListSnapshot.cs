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
        public List<T> List;

        public ListSnapshot(ListSnapshotPool<T> pool, List<T> list) {
            Pool = pool;
            List = list;
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

        public ListSnapshot<T> Get(List<T> list) {
            if (Bag.TryTake(out ListSnapshot<T>? snapshot)) {
                snapshot.Disposed = false;
                snapshot.List = list;
                return snapshot;
            }

            return new ListSnapshot<T>(this, list);
        }

        public void Add(ListSnapshot<T> snapshot) {
            if (snapshot.Disposed || (MaxCount != 0 && Count >= MaxCount))
                return;
            Count++;
            snapshot.Disposed = true;
            snapshot.List = Dummy<T>.EmptyList;
            Bag.Add(snapshot);
        }

    }

    public static class ListSnapshotStaticPool<T> {

        public static ListSnapshotPool<T> Pool = new ListSnapshotPool<T>();

    }

    public static class ListSnapshotStaticPool {

        public static ListSnapshot<T> ToSnapshot<T>(this IEnumerable<T> list, RWLock rwlock) {
            List<T> copy;
            using (rwlock.R())
                copy = new List<T>(list);
            return ListSnapshotStaticPool<T>.Pool.Get(copy);
        }

        public static ListSnapshot<T> ToSnapshot<T>(this IEnumerable<T> list, object rlock) {
            List<T> copy;
            if (rlock != null) {
                lock (rlock)
                    copy = new List<T>(list);
            } else {
                copy = new List<T>(list);
            }
            return ListSnapshotStaticPool<T>.Pool.Get(copy);
        }

        public static ListSnapshot<T> ToSnapshot<T>(this IEnumerable<T> list) {
            List<T> copy;
            lock (list)
                copy = new List<T>(list);
            return ListSnapshotStaticPool<T>.Pool.Get(copy);
        }

    }
}
