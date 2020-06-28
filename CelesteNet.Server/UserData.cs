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
    public class UserData : IDisposable {

        public readonly CelesteNetServer Server;

        public UserData(CelesteNetServer server) {
            Server = server;
        }

        public void Start() {
            Logger.Log(LogLevel.INF, "userdata", "Startup");
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "userdata", "Shutdown");
        }

    }
}
