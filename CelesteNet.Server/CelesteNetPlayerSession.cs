using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Options;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetPlayerSession : IDisposable {

        public readonly CelesteNetServer Server;
        public readonly CelesteNetConnection Con;
        public readonly uint ID;

        public DataPlayerInfo? PlayerInfo => Server.Data.TryGetRef(ID, out DataPlayerInfo? value) ? value : null;
        public Channel Channel => Server.Channels.Get(this);

        public CelesteNetPlayerSession(CelesteNetServer server, CelesteNetConnection con, uint id) {
            Server = server;
            Con = con;
            ID = id;

            Server.Data.RegisterHandlersIn(this);
        }

        public void Start<T>(DataHandshakeClient<T> handshake) where T : DataHandshakeClient<T> {
            Logger.Log(LogLevel.INF, "playersession", $"Startup #{ID} {Con}");
            lock (Server.Connections) {
                Server.PlayersByCon[Con] = this;
                Server.PlayersByID[ID] = this;
            }

            string name = handshake.Name;
            // TODO: Handle names starting with # as "keys"

            name = name.Sanitize();
            if (name.Length > Server.Settings.MaxNameLength)
                name = name.Substring(0, Server.Settings.MaxNameLength);

            string fullName = name;

            lock (Server.Connections)
                for (int i = 2; Server.PlayersByCon.Values.Any(other => other.PlayerInfo?.FullName == fullName); i++)
                    fullName = $"{name}#{i}";

            DataPlayerInfo playerInfo = new DataPlayerInfo {
                ID = ID,
                Name = name,
                FullName = fullName
            };
            Server.Data.SetRef(playerInfo);

            Logger.Log(LogLevel.INF, "playersession", playerInfo.ToString());

            Con.Send(new DataHandshakeServer {
                PlayerInfo = playerInfo
            });

            lock (Server.Connections) {
                foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                    if (other == this)
                        continue;

                    other.Con.Send(playerInfo);

                    DataPlayerInfo? otherInfo = other.PlayerInfo;
                    if (otherInfo == null)
                        continue;

                    Con.Send(otherInfo);

                    foreach (DataType bound in Server.Data.GetBoundRefs(otherInfo))
                        if (!(bound is IDataPlayerState) || other.Channel.ID == 0)
                            Con.Send(bound);
                }
            }

            ResendPlayerStates();

            foreach (DataType data in Server.Data.GetAllStatic())
                Con.Send(data);

            Server.InvokeOnSessionStart(this);
        }

        public void ResendPlayerStates() {
            Channel channel = Channel;

            lock (Server.Connections) {
                foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                    if (other == this)
                        continue;

                    foreach (DataType bound in Server.Data.GetBoundRefs(PlayerInfo))
                        if (!(bound is IDataPlayerState) || channel == other.Channel)
                            other.Con.Send(bound);

                    DataPlayerInfo? otherInfo = other.PlayerInfo;
                    if (otherInfo == null)
                        continue;

                    foreach (DataType bound in Server.Data.GetBoundRefs(otherInfo))
                        if (!(bound is IDataPlayerState) || channel == other.Channel)
                            Con.Send(bound);
                }
            }
        }

        public bool IsSameArea(CelesteNetPlayerSession other)
            => Server.Data.TryGetBoundRef(PlayerInfo, out DataPlayerState? state) && state != null && IsSameArea(Channel, state, other);

        public bool IsSameArea(Channel channel, DataPlayerState? state, CelesteNetPlayerSession other)
            =>  state != null &&
                other.Channel == channel &&
                Server.Data.TryGetBoundRef(other.PlayerInfo, out DataPlayerState? otherState) &&
                otherState != null &&
                otherState.SID == state.SID &&
                otherState.Mode == state.Mode;

        public event Action<CelesteNetPlayerSession, DataPlayerInfo?>? OnEnd;

        public void Dispose() {
            Logger.Log(LogLevel.INF, "playersession", $"Shutdown #{ID} {Con}");

            DataPlayerInfo? playerInfoLast = PlayerInfo;

            lock (Server.Connections) {
                Server.PlayersByCon.Remove(Con);
                Server.PlayersByID.Remove(ID);
            }

            Server.Broadcast(new DataPlayerInfo {
                ID = ID
            });

            Server.Data.FreeRef<DataPlayerInfo>(ID);
            Server.Data.FreeOrder<DataPlayerFrame>(ID);

            Server.Data.UnregisterHandlersIn(this);

            OnEnd?.Invoke(this, playerInfoLast);
        }


        #region Handlers

        public bool Filter(CelesteNetConnection con, DataPlayerInfo updated) {
            // Make sure that a player can only update their own info.
            if (con != Con)
                return true;

            DataPlayerInfo? old = PlayerInfo;
            if (old == null)
                return true;

            updated.ID = old.ID;
            updated.Name = old.Name;
            updated.FullName = old.FullName;

            return true;
        }

        public bool Filter(CelesteNetConnection con, DataType data) {
            if (con != Con)
                return true;

            if (data is IDataPlayerState bound)
                bound.Player = PlayerInfo;

            if (data is IDataPlayerUpdate update)
                update.Player = PlayerInfo;

            return true;
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo updated) {
            if (con != Con)
                return;

            lock (Server.Connections) {
                foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                    if (other == this)
                        continue;

                    other.Con.Send(updated);
                }
            }
        }

        public void Handle(CelesteNetConnection con, DataType data) {
            if (con != Con)
                return;

            if (!Server.Data.TryGetBoundRef(PlayerInfo, out DataPlayerState? state))
                state = null;

            if (data is IDataBoundRef<DataPlayerInfo> ||
                data is IDataPlayerState ||
                data is IDataPlayerUpdate) {
                Channel channel = Channel;

                lock (Server.Connections) {
                    foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                        if (other == this)
                            continue;

                        if ((data is IDataPlayerState || data is IDataPlayerUpdate) && !IsSameArea(channel, state, other))
                            continue;

                        other.Con.Send(data);
                    }
                }
            }
        }

        #endregion

    }
}
