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
    public class StringMap {

        public ConcurrentDictionary<ushort, string> MapToValue = new ConcurrentDictionary<ushort, string>();
        public ConcurrentDictionary<string, ushort> MapToID = new ConcurrentDictionary<string, ushort>();
        public Dictionary<string, ushort> Pending = new Dictionary<string, ushort>();

        public ushort PromotionCount = 3;
        public ushort MinLength = 8;

        public string Get(ushort id)
            => MapToValue[id];

        public bool TryMap(string? value, out ushort id) {
            if (value == null || value.Length <= MinLength) {
                id = 0;
                return false;
            }

            if (MapToID.TryGetValue(value, out id))
                return true;

            lock (Pending) {
                if (MapToID.ContainsKey(value))
                    return false;

                if (!Pending.TryGetValue(value, out ushort count))
                    count = 0;
                if (++count > PromotionCount)
                    count = PromotionCount;
                Pending[value] = count;
                return false;
            }
        }

    }
}
