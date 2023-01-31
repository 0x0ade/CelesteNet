using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Options;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class Channels : IDisposable {

        public const string NameDefault = "main";
        public const string NamePrivate = "!<private>";
        public const string PrefixPrivate = "!";

        public readonly CelesteNetServer Server;

        public readonly Channel Default;
        public readonly List<Channel> All = new();
        public readonly Dictionary<uint, Channel> ByID = new();
        public readonly Dictionary<string, Channel> ByName = new();
        public uint NextID = (uint) (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);

        public Channels(CelesteNetServer server) {
            Server = server;

            Server.Data.RegisterHandlersIn(this);

            Default = new(this, NameDefault, 0);
        }

        public void Start() {
            Logger.Log(LogLevel.INF, "channels", "Startup");
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "channels", "Shutdown");
        }

        public bool SessionStartupMove(CelesteNetPlayerSession session) {
            if (Server.UserData.TryLoad(session.UID, out LastChannelUserInfo last) &&
                last.Name != NameDefault) {
                (Channel curr, Channel prev) = Move(session, last.Name);
                return curr != prev;
            } else {
                Default.Add(session);
                BroadcastList();
            }
            return false;
        }

        public void SendListTo(CelesteNetPlayerSession session) {
            Channel own = session.Channel;

            List<DataChannelList.Channel> channels;
            lock (All) {
                channels = new(All.Count);
                foreach (Channel c in All) {
                    if (c.IsPrivate && c != own)
                        continue;
                    uint[] players;
                    using (c.Lock.R()) {
                        players = new uint[c.Players.Count];
                        int i = 0;
                        foreach (CelesteNetPlayerSession p in c.Players)
                            players[i++] = p.SessionID;
                    }
                    channels.Add(new DataChannelList.Channel {
                        Name = c.Name,
                        ID = c.ID,
                        Players = players
                    });
                }
            }

            session.Con.Send(new DataChannelList {
                List = channels.ToArray()
            });
        }

        public Action<Channels>? OnBroadcastList;

        public void BroadcastList() {
            using (ListSnapshot<Channel> snapshot = All.ToSnapshot())
                foreach (Channel c in snapshot)
                    c.RemoveStale();

            OnBroadcastList?.Invoke(this);

            lock (All)
                using (Server.ConLock.R())
                    foreach (CelesteNetPlayerSession session in Server.Sessions)
                        SendListTo(session);
        }

        public Tuple<Channel, Channel> Move(CelesteNetPlayerSession session, string name) {
            name = name.Sanitize();
            if (name.Length > Server.Settings.MaxChannelNameLength)
                name = name.Substring(0, Server.Settings.MaxChannelNameLength);
            if (name == NamePrivate)
                throw new Exception("Invalid private channel name.");

            lock (All) {
                Channel prev = session.Channel;

                Channel c;

                if (ByName.TryGetValue(name, out Channel? existing)) {
                    c = existing;
                    if (prev == c)
                        return Tuple.Create(c, c);

                } else {
                    c = new(this, name, NextID++);
                }

                prev.Remove(session);

                if (session.PlayerInfo != null)
                    c.Add(session);

                DataInternalBlob move = new(Server.Data, new DataChannelMove {
                    Player = session.PlayerInfo
                });
                session.Con.Send(move);
                using (prev.Lock.R())
                    foreach (CelesteNetPlayerSession other in prev.Players)
                        other.Con.Send(move);

                BroadcastList();

                session.ResendPlayerStates();

                if (!Server.UserData.GetKey(session.UID).IsNullOrEmpty()) {
                    Server.UserData.Save(session.UID, new LastChannelUserInfo {
                        Name = name
                    });
                }

                return Tuple.Create(prev, c);
            }
        }

    }

    public class Channel : IDisposable {
        public readonly Channels Ctx;
        public readonly string Name;
        public readonly uint ID;
        public readonly RWLock Lock = new();
        public readonly HashSet<CelesteNetPlayerSession> Players = new();

        public readonly bool IsPrivate;
        public readonly string PublicName;

        public Channel(Channels ctx, string name, uint id) {
            Ctx = ctx;
            Name = name;
            ID = id;

            if (IsPrivate = name.StartsWith(Channels.PrefixPrivate)) {
                PublicName = Channels.NamePrivate;

            } else {
                PublicName = name;
            }

            lock (Ctx.All) {
                Ctx.All.Add(this);
                Ctx.ByName[Name] = this;
                Ctx.ByID[ID] = this;
            }
        }

        public void RemoveStale() {
            using (Lock.W()) {
                List<CelesteNetPlayerSession> stale = new();
                foreach (CelesteNetPlayerSession session in Players)
                    if (session.PlayerInfo == null)
                        stale.Add(session);
                foreach (CelesteNetPlayerSession session in stale)
                    Remove(session);
            }
        }

        public void Add(CelesteNetPlayerSession session) {
            using (Lock.W())
                if (!Players.Add(session))
                    return;

            session.Channel = this;
            session.OnEnd += RemoveByDC;

            if (session.PlayerInfo == null)
                Remove(session);
        }

        public void Remove(CelesteNetPlayerSession session) {
            using (Lock.W())
                if (!Players.Remove(session))
                    return;

            // Hopefully nobody will get stuck in channel limbo...
            session.OnEnd -= RemoveByDC;

            if (ID == 0)
                return;

            lock (Ctx.All) {
                if (Players.Count > 0)
                    return;

                Ctx.All.Remove(this);
                Ctx.ByName.Remove(Name);
                Ctx.ByID.Remove(ID);
            }
        }

        private void RemoveByDC(CelesteNetPlayerSession session, DataPlayerInfo? lastInfo) {
            Remove(session);
            Ctx.BroadcastList();
        }

        public void Dispose() {
            Lock.Dispose();
        }
    }

    public class LastChannelUserInfo {
        public string Name = "main";
    }
}
