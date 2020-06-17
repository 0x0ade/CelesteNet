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

            name = name.Replace("\r", "").Replace("\n", "").Trim();
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
                        Con.Send(bound);
                }
            }

            Server.InvokeOnSessionStart(this);
        }

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

            if (data is IDataBoundRef<DataPlayerInfo> bound)
                bound.ID = ID;

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

            if (PlayerInfo == null || !Server.Data.TryGetBoundRef(PlayerInfo, out DataPlayerState? state))
                state = null;

            if (data is IDataBoundRef<DataPlayerInfo> ||
                data is IDataPlayerUpdate) {
                if (state == null)
                    return;

                lock (Server.Connections) {
                    foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                        if (other == this)
                            continue;

                        if (data is IDataPlayerUpdate && (
                            other.PlayerInfo == null ||
                            !Server.Data.TryGetBoundRef(other.PlayerInfo, out DataPlayerState? otherState) ||
                            otherState == null ||
                            otherState.Channel != state.Channel ||
                            otherState.SID != state.SID ||
                            otherState.Mode != state.Mode
                        ))
                            continue;

                        other.Con.Send(data);
                    }
                }
            }
        }

        #endregion

    }
}
