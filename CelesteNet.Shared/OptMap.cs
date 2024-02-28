using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet
{
    /*
    Generalized StringMaps into OptMaps
    -Popax21

    Coming up with this setup from scratch took me a few attempts and way too much time in general.
    StringMaps are used per subconnection (for TCPUDP, that's one for TCP and one for UDP).
    When A CountReads string S often enough, A asks B to map it to I.
    B then sends I instead of S, which A can turn back into S.
    Same goes into the opposite direction too, albeit asymmetrically.
    If B ignores the <S, I> mapping request, both peers can still communicate fine using S.
    This isn't done globally as to not introduce wasting every peer's time waiting for mappings to be synced.
    This isn't done connection-wise as connections can have multiple subconnections (TCPUDP: TCP and UDP) which aren't in sync.
    This could be improved with acks and resending the mapping in the future.
    Cleaning up the Counting dictionary could be improved too, as it currently halts.
    -jade
    */
    public class OptMap<T> where T : class {

        public readonly string Name;

        public readonly ConcurrentDictionary<int, T> MapRead = new();
        public readonly ConcurrentDictionary<T, int> MapWrite = new();

        private readonly Dictionary<T, int> Counting = new();
        private readonly HashSet<T> Pending = new();
        private readonly HashSet<T> MappedRead = new();
        private readonly List<T> CountingUpdateKeys = new();
        private readonly List<int> CountingUpdateValues = new();

        private int NextID;

        public int PromotionCount = 18;
        public int PromotionTreshold = 10;
        public int MaxCounting = 32;
        public int DemotionScore = 3;

        public OptMap(string name) {
            Name = name;
        }

        public T Get(int id)
            => MapRead[id];

        public void CountRead(T value) {
            lock (Pending) {
                if (MappedRead.Contains(value) || Pending.Contains(value))
                    return;

                if (!Counting.TryGetValue(value, out int count))
                    count = 0;
                if (++count >= PromotionCount) {
                    Counting.Remove(value);
                    Pending.Add(value);
                } else {
                    Counting[value] = count;
                }

                if (Counting.Count >= MaxCounting)
                    Cleanup();
            }
        }

        public void Cleanup() {
            lock (Pending) {
                foreach (KeyValuePair<T, int> entry in Counting) {
                    int score = entry.Value - DemotionScore;
                    if (score <= 0) {
                        CountingUpdateKeys.Add(entry.Key);
                        CountingUpdateValues.Add(0);
                    } else if (score >= PromotionTreshold) {
                        CountingUpdateKeys.Add(entry.Key);
                        CountingUpdateValues.Add(0);
                        Pending.Add(entry.Key);
                    } else {
                        CountingUpdateKeys.Add(entry.Key);
                        CountingUpdateValues.Add(score);
                    }
                }
                for (int i = 0; i < CountingUpdateKeys.Count; i++) {
                    T key = CountingUpdateKeys[i];
                    int value = CountingUpdateValues[i];
                    if (value == 0)
                        Counting.Remove(key);
                    else
                        Counting[key] = value;
                }
                CountingUpdateKeys.Clear();
                CountingUpdateValues.Clear();
            }
        }

        public List<Tuple<T, int>> PromoteRead() {
            lock (Pending) {
                if (Pending.Count == 0)
                    return Dummy<Tuple<T, int>>.EmptyList;

                List<Tuple<T, int>> added = new();
                foreach (T value in Pending) {
                    int id = NextID++;
                    MapRead[id] = value;
                    MappedRead.Add(value);
                    added.Add(Tuple.Create(value, id));
                }
                Pending.Clear();
                return added;
            }
        }

        public void RegisterWrite(T value, int id) {
            MapWrite[value] = id;
        }

        public bool TryMap(T? value, out int id) {
            if (value == null) {
                id = 0;
                return false;
            }

            return MapWrite.TryGetValue(value, out id);
        }

    }
}
