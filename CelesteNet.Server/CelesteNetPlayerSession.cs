using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Control;
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

        public DataPlayerInfo PlayerInfo => Server.Data.TryGetRef(ID, out DataPlayerInfo value) ? value : null;

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
            Server.Control.BroadcastCMD("update", "/status");

            string name = handshake.Name;
            // TODO: Handle names starting with # as "keys"

            name = name.Replace("\r", "").Replace("\n", "").Trim();
            if (name.Length > Server.Settings.MaxNameLength)
                name = name.Substring(0, Server.Settings.MaxNameLength);

            string fullName = name;

            lock (Server.Connections)
                for (int i = 2; Server.PlayersByCon.Values.Any(other => other.PlayerInfo?.FullName == fullName); i++)
                    fullName = $"{name}#{i}";

            Server.Data.SetRef(new DataPlayerInfo {
                ID = ID,
                Name = name,
                FullName = fullName
            });

            Logger.Log(LogLevel.INF, "playersession", PlayerInfo.ToString());
            Server.Control.BroadcastCMD("update", "/players");

            Con.Send(new DataHandshakeServer {
                Version = CelesteNetUtils.Version,
                PlayerInfo = PlayerInfo
            });

            lock (Server.Connections) {
                foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                    if (other == this)
                        continue;

                    other.Con.Send(PlayerInfo);
                    Con.Send(other.PlayerInfo);
                    foreach (DataType bound in Server.Data.GetBoundRefs(other.PlayerInfo))
                        Con.Send(bound);
                }
            }

            Server.Chat.Broadcast(Server.Settings.MessageGreeting.InjectSingleValue("player", fullName));
            Server.Chat.Send(this, Server.Settings.MessageMOTD);
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "playersession", $"Shutdown #{ID} {Con}");

            string fullName = PlayerInfo?.FullName;
            if (!string.IsNullOrEmpty(fullName))
                Server.Chat.Broadcast(Server.Settings.MessageLeave.InjectSingleValue("player", fullName));

            lock (Server.Connections) {
                Server.PlayersByCon.Remove(Con);
                Server.PlayersByID.Remove(ID);
            }

            Server.Broadcast(new DataPlayerInfo {
                ID = ID
            });

            Server.Data.FreeRef<DataPlayerInfo>(ID);

            Server.Control.BroadcastCMD("update", "/status");
            Server.Control.BroadcastCMD("update", "/players");
        }


        #region Handlers

        public bool Filter(CelesteNetConnection con, DataPlayerInfo updated) {
            // Make sure that a player can only update their own info.
            if (con != Con)
                return true;

            DataPlayerInfo old = PlayerInfo;
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

            if (data is IDataBoundRefType<DataPlayerInfo> bound)
                bound.ID = ID;

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

            if (data is IDataBoundRefType<DataPlayerInfo> bound) {
                lock (Server.Connections) {
                    foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                        if (other == this)
                            continue;

                        other.Con.Send(data);
                    }
                }
            }
        }

        #endregion

    }
}
