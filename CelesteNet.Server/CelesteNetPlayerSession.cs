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

        public string Name;
        public string FullName;

        public CelesteNetPlayerSession(CelesteNetServer server, CelesteNetConnection con, uint id) {
            Server = server;
            Con = con;
            ID = id;

            foreach (MethodInfo method in GetType().GetMethods()) {
                if (method.Name != "Handle")
                    continue;

                ParameterInfo[] args = method.GetParameters();
                if (args.Length != 1)
                    continue;

                Type argType = args[0].ParameterType;
                if (!argType.IsCompatible(typeof(DataType)))
                    continue;

                Server.Data.RegisterHandler(argType, (other, data) => {
                    if (con != other)
                        return;
                    method.Invoke(this, new object[] { data });
                });
            }
        }

        public void Start<T>(DataHandshakeClient<T> handshake) where T : DataHandshakeClient<T> {
            Logger.Log(LogLevel.INF, "playersession", $"Startup #{ID} {Con}");
            lock (Server.Players)
                Server.Players[Con] = this;
            Server.Control.BroadcastCMD("update", "/status");

            // TODO: Handle names starting with # as "keys"
            Name = handshake.Name;
            FullName = $"{Name}#{ID}";

            Logger.Log(LogLevel.INF, "playersession", $"#{ID} is {FullName}");
            Server.Control.BroadcastCMD("update", "/players");

            Con.Send(new DataHandshakeServer {
                Version = CelesteNetUtils.Version,

                PlayerInfo = Server.Data.SetRef(new DataPlayerInfo {
                    ID = ID,
                    Name = Name,
                    FullName = FullName
                })
            });

            lock (Server.Players) {
                foreach (DataPlayerInfo other in Server.Data.GetRefs<DataPlayerInfo>()) {
                    if (other.ID == ID)
                        continue;

                    Con.Send(other);
                }
            }

            // TODO: Welcome the player.
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "playersession", $"Shutdown #{ID} {Con}");

            lock (Server.Players) {
                Server.Players.Remove(Con);
                Server.Broadcast(new DataPlayerInfo {
                    ID = ID
                });
            }

            Server.Data.FreeRef<DataPlayerInfo>(ID);

            Server.Control.BroadcastCMD("update", "/status");
            Server.Control.BroadcastCMD("update", "/players");
        }


        #region Handlers

        #endregion

    }
}
