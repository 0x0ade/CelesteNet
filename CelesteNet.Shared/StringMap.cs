using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectFactories;

namespace Celeste.Mod.CelesteNet {
    /*
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
    public class StringMap {

        private class CountingUpdate {
            public string Key;
            public int? Value;
            public CountingUpdate(string key, int? value) {
                Key = key;
                Value = value;
            }
        }

        public const string ConnectionFeature = "stringmap";

        public readonly string Name;

        public readonly ConcurrentDictionary<int, string> MapRead = new();
        public readonly ConcurrentDictionary<string, int> MapWrite = new();

        private readonly Dictionary<string, int> Counting = new();
        private readonly HashSet<string> Pending = new();
        private readonly HashSet<string> MappedRead = new();
        private readonly List<CountingUpdate> CountingUpdates = new();

        private int NextID;

        public int PromotionCount = 18;
        public int PromotionTreshold = 10;
        public int MaxCounting = 32;
        public int DemotionScore = 3;
        public int MinLength = 4;

        public StringMap(string name) {
            Name = name;
        }

        public string Get(int id)
            => MapRead[id];

        public void CountRead(string value) {
            if (value.Length <= MinLength)
                return;

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
                foreach (KeyValuePair<string, int> entry in Counting) {
                    int score = entry.Value - DemotionScore;
                    if (score <= 0) {
                        CountingUpdates.Add(new(entry.Key, null));
                    } else if (score >= PromotionTreshold) {
                        CountingUpdates.Add(new(entry.Key, null));
                        Pending.Add(entry.Key);
                    } else {
                        CountingUpdates.Add(new(entry.Key, score));
                    }
                }
                foreach (CountingUpdate update in CountingUpdates) {
                    if (update.Value == null)
                        Counting.Remove(update.Key);
                    else
                        Counting[update.Key] = update.Value.Value;
                }
                CountingUpdates.Clear();
            }
        }

        public List<Tuple<string, int>> PromoteRead() {
            lock (Pending) {
                if (Pending.Count == 0)
                    return Dummy<Tuple<string, int>>.EmptyList;

                List<Tuple<string, int>> added = new();
                foreach (string value in Pending) {
                    int id = NextID++;
                    MapRead[id] = value;
                    MappedRead.Add(value);
                    added.Add(Tuple.Create(value, id));
                }
                Pending.Clear();
                return added;
            }
        }

        public void RegisterWrite(string value, int id) {
            MapWrite[value] = id;
        }

        public bool TryMap(string? value, out int id) {
            if (value == null || value.Length <= MinLength) {
                id = 0;
                return false;
            }

            return MapWrite.TryGetValue(value, out id);
        }

    }
}
