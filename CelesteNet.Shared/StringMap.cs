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
    When A CountReads string S often enough, the A asks B to map it to I.
    B then sends I instead of S, which A can turn back into S.
    Same goes into the opposite direction too, albeit asymmetrically.
    If B ignores the <S, I> mapping request, both peers can still communicate fine using S.
    This isn't done globally as to not introduce wasting every peer's time waiting for mappings to be synced.
    This isn't done connection-wise as connections can have multiple subconnections (TCPUDP: TCP and UDP) which aren't in sync.
    This could be improved with acks and resending the mapping in the future.
    -jade
    */
    public class StringMap {

        public const string ConnectionFeature = "stringmap";

        public readonly string Name;

        public readonly ConcurrentDictionary<ushort, string> MapRead = new ConcurrentDictionary<ushort, string>();
        public readonly ConcurrentDictionary<string, ushort> MapWrite = new ConcurrentDictionary<string, ushort>();

        private readonly Dictionary<string, ushort> Counting = new Dictionary<string, ushort>();
        private readonly HashSet<string> Pending = new HashSet<string>();
        private readonly HashSet<string> MappedRead = new HashSet<string>();

        private ushort NextID;

        public ushort PromotionCount = 3;
        public ushort MinLength = 8;

        public StringMap(string name) {
            Name = name;
        }

        public string Get(ushort id)
            => MapRead[id];

        public void CountRead(string value) {
            if (value.Length <= MinLength)
                return;

            lock (Pending) {
                if (MappedRead.Contains(value) || Pending.Contains(value))
                    return;

                if (!Counting.TryGetValue(value, out ushort count))
                    count = 0;
                if (++count > PromotionCount) {
                    Counting.Remove(value);
                    Pending.Add(value);
                } else {
                    Counting[value] = count;
                }
            }
        }

        public List<Tuple<string, ushort>> PromoteRead() {
            lock (Pending) {
                if (Pending.Count == 0)
                    return Dummy<Tuple<string, ushort>>.EmptyList;

                List<Tuple<string, ushort>> added = new List<Tuple<string, ushort>>();
                foreach (string value in Pending) {
                    ushort id = NextID++;
                    MapRead[id] = value;
                    MappedRead.Add(value);
                    added.Add(Tuple.Create(value, id));
                }
                Pending.Clear();
                return added;
            }
        }

        public void RegisterWrite(string value, ushort id) {
            MapWrite[value] = id;
        }

        public bool TryMap(string? value, out ushort id) {
            if (value == null || value.Length <= MinLength) {
                id = 0;
                return false;
            }

            return MapWrite.TryGetValue(value, out id);
        }

    }
}
