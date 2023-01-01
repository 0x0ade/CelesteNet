using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Filter {
    public class FilterModule : CelesteNetServerModule<FilterSettings> {

        public class PacketCounts {
            public int InCount = 0;
            public int OutCount = 0;
            public int InDrop = 0;
            public int OutDrop = 0;
        }

        private readonly ConcurrentDictionary<Tuple<CelesteNetConnection, string>, PacketCounts> Counts = new ();

        private Random rng = new ();

        public override void Init(CelesteNetServerModuleWrapper wrapper) {
            base.Init(wrapper);

            if (!Settings.Enabled)
                return;

            Server.OnConnect += OnConnect;
            using (Server.ConLock.R())
                foreach (CelesteNetConnection con in Server.Connections)
                    OnConnect(Server, con);
        }

        public override void Dispose() {
            base.Dispose();

            if (!Settings.Enabled)
                return;

            Server.OnConnect -= OnConnect;
            using (Server.ConLock.R())
                foreach (CelesteNetConnection con in Server.Connections)
                    con.OnDisconnect -= OnDisconnect;
        }

        private void OnConnect(CelesteNetServer server, CelesteNetConnection con) {
            con.OnSendFilter += ConSendFilter;
            ((ConPlusTCPUDPConnection) con).OnDequeueFilter += ConSendFilter;
            con.OnDisconnect += OnDisconnect;
        }

        private void OnDisconnect(CelesteNetConnection con) {
            con.OnSendFilter -= ConSendFilter;
            ((ConPlusTCPUDPConnection) con).OnDequeueFilter -= ConSendFilter;
            if (Settings.Enabled && Settings.PrintSessionStatsOnEnd) {
                CelesteNetConnection c;
                string type;
                foreach (var kv in Counts) {
                    (c, type) = kv.Key;
                    PacketCounts val = kv.Value;

                    if (c != con)
                        continue;

                    LogStats($"{con.ID} - packets {type,14}", val, outbound: true);
                    LogStats($"{con.ID} - packets {type,14}", val, outbound: false);
                }
                if (Settings.PrintAllSessionStats) {
                    foreach (var kv in Counts) {
                        (c, type) = kv.Key;
                        PacketCounts val = kv.Value;

                        if (c != con)
                            continue;

                        if (val.OutCount == 0 && val.OutDrop == 0)
                            LogStats($"{con.ID} - packets {type,14}", val, outbound: true, forcePrint: true);
                        if (val.InCount == 0 && val.InDrop == 0)
                            LogStats($"{con.ID} - packets {type,14}", val, outbound: false, forcePrint: true);
                    }
                }
            }
        }

        private void LogStats(string info, PacketCounts val, bool outbound, bool forcePrint = false) {
            int count = outbound ? val.OutCount : val.InCount;
            int dropped = outbound ? val.OutDrop : val.InDrop;
            if (forcePrint || count > 0 || dropped > 0)
                Logger.Log(LogLevel.INF, "filtermod", $"{info} - {(outbound ? "Outbound" : " Inbound")}: {count,3} (dropped {dropped,3})");
        }

        public bool Filter(CelesteNetConnection con, DataType data) {
            if (!Settings.Enabled)
                return true;

            string type = data.GetTypeID(Server.Data);

            PacketCounts? counts = null;
            Tuple<CelesteNetConnection, string>? key = null;

            if (Settings.CollectAllSessionStats) {
                key = Tuple.Create(con, type);
                counts = Counts.GetOrAdd(key, new PacketCounts());
                counts.InCount++;
            }

            // check if type matches inbound filter rule
            if (type.IsNullOrEmpty() || !Settings.FiltersInbound.TryGetValue(type, out FilterSettings.FilterDef filter))
                return true;

            // handle the filter in inner method, provide tuple for dictionaries
            key ??= Tuple.Create(con, type);
            return FilterInner(ref filter, ref key, ref counts);
        }

        private bool FilterInner(ref FilterSettings.FilterDef filter, ref Tuple<CelesteNetConnection, string> key, ref PacketCounts? counts) {

            if (counts == null) {
                counts = Counts.GetOrAdd(key, new PacketCounts());
                counts.InCount++;
            }

            bool belowThreshold = counts.InCount <= filter.DropThreshold;
            bool aboveLimit = filter.DropLimit > 0 && counts.InDrop >= filter.DropLimit;

            if (belowThreshold || aboveLimit)
                return true;

            if (rng.Next(100) > filter.DropChance)
                return true;

            Logger.Log(LogLevel.WRN, "filtermod", $"Con #{key.Item1.ID} Filter INBOUND '{key.Item2}' - dropped ({filter.DropThreshold}) < {counts.InCount} / ({filter.DropLimit}) < {counts.InDrop}");
            counts.InDrop++;
            return false;
        }

        public bool ConSendFilter(CelesteNetConnection con, DataType data) {
            if (!Settings.Enabled)
                return true;

            string type = data.GetTypeID(Server.Data);

            PacketCounts? counts = null;
            Tuple<CelesteNetConnection, string>? key = null;

            if (Settings.CollectAllSessionStats) {
                key = Tuple.Create(con, type);
                counts = Counts.GetOrAdd(key, new PacketCounts());
                counts.OutCount++;
            }

            // check if type matches outbound filter rule
            if (type.IsNullOrEmpty() || !Settings.FiltersOutbound.TryGetValue(type, out FilterSettings.FilterDef filter))
                return true;

            // handle the filter in inner method, provide tuple for dictionaries
            key ??= Tuple.Create(con, type);
            return ConSendFilterInner(ref filter, ref key, ref counts);
        }

        private bool ConSendFilterInner(ref FilterSettings.FilterDef filter, ref Tuple<CelesteNetConnection, string> key, ref PacketCounts? counts) {

            if (counts == null) {
                counts = Counts.GetOrAdd(key, new PacketCounts());
                counts.OutCount++;
            }

            bool belowThreshold = counts.OutCount <= filter.DropThreshold;
            bool aboveLimit = filter.DropLimit > 0 && counts.OutDrop > filter.DropLimit;

            if (belowThreshold || aboveLimit)
                return true;

            if (rng.Next(100) > filter.DropChance)
                return true;

            Logger.Log(LogLevel.WRN, "filtermod", $"Con #{key.Item1.ID} Filter OUTBOUND '{key.Item2}' - dropped ({filter.DropThreshold}) < {counts.OutCount} / ({filter.DropLimit}) <= {counts.OutDrop}");
            counts.OutDrop++;
            return false;
        }

    }
}
