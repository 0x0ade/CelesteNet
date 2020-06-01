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
    public class CelesteNetSession : IDisposable {

        public readonly CelesteNetServer Server;
        public readonly CelesteNetConnection Con;
        public readonly uint ID;

        public CelesteNetSession(CelesteNetServer server, CelesteNetConnection con, uint id) {
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

        public void Start() {
            Logger.Log(LogLevel.INF, "session", $"Startup #{ID} {Con}");

            Con.Send(new DataHandshakeServer {
                Version = CelesteNetUtils.Version
            });
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "session", $"Shutdown #{ID} {Con}");
        }


        #region Handlers

        #endregion

    }
}
