using Celeste.Mod.CelesteNet.Server.Control;
using Celeste.Mod.CelesteNet.Shared.DataTypes;
using Mono.Options;
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
    public class ChatServer : IDisposable {

        public readonly CelesteNetServer Server;

        public LinkedList<DataChat> ChatLog = new LinkedList<DataChat>();

        public ChatServer(CelesteNetServer server) {
            Server = server;
        }

        public void Start() {
            Logger.Log(LogLevel.INF, "chat", "Startup");
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "chat", "Shutdown");
        }


        public void Broadcast(string text) {

        }

    }
}
