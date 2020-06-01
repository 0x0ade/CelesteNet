using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Control;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetServer : IDisposable {

        public readonly CelesteNetServerSettings Settings;

        public readonly DataContext Data;
        public readonly Frontend Control;
        public readonly ChatServer Chat;
        public readonly TCPUDPServer TCPUDP;

        public readonly HashSet<CelesteNetConnection> Connections = new HashSet<CelesteNetConnection>();

        public uint PlayerCounter = 1;
        public readonly Dictionary<CelesteNetConnection, CelesteNetPlayerSession> Players = new Dictionary<CelesteNetConnection, CelesteNetPlayerSession>();

        private ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

        private bool _IsAlive;
        public bool IsAlive {
            get => _IsAlive;
            set {
                if (_IsAlive == value)
                    return;

                _IsAlive = value;
                if (value)
                    ShutdownEvent.Reset();
                else
                    ShutdownEvent.Set();
            }
        }

        public CelesteNetServer()
            : this(new CelesteNetServerSettings()) {
        }

        public CelesteNetServer(CelesteNetServerSettings settings) {
            Settings = settings;

            Data = new DataContext();
            Data.RegisterHandlersIn(this);
            Control = new Frontend(this);
            Chat = new ChatServer(this);
            TCPUDP = new TCPUDPServer(this);
        }

        public void Start() {
            if (IsAlive)
                return;

            Logger.Log(LogLevel.CRI, "main", $"Startup");
            IsAlive = true;

            Control.Start();
            Chat.Start();
            TCPUDP.Start();

            Logger.Log(LogLevel.CRI, "main", "Ready");
        }

        public void Wait() {
            WaitHandle.WaitAny(new WaitHandle[] { ShutdownEvent });
            ShutdownEvent.Dispose();
        }

        public void Dispose() {
            if (!IsAlive)
                return;

            Logger.Log(LogLevel.CRI, "main", "Shutdown");
            IsAlive = false;

            Control.Dispose();
            Chat.Dispose();
        }


        public void HandleConnect(CelesteNetConnection con) {
            Logger.Log(LogLevel.INF, "main", $"New connection: {con}");
            con.SendKeepAlive = true;
            lock (Connections)
                Connections.Add(con);
            con.OnDisconnect += HandleDisconnect;
            Control.BroadcastCMD("update", "/status");
        }

        public void HandleDisconnect(CelesteNetConnection con) {
            Logger.Log(LogLevel.INF, "main", $"Disconnecting: {con}");
            lock (Connections)
                Connections.Remove(con);
            lock (Players)
                Players.Remove(con);
            Control.BroadcastCMD("update", "/status");
            Control.BroadcastCMD("update", "/players");
        }


        public Stream OpenContent(string path) {
            try {
                string dir = Path.GetFullPath(Settings.ContentRoot);
                string pathFS = Path.GetFullPath(Path.Combine(dir, path));
                if (pathFS.StartsWith(dir) && File.Exists(pathFS))
                    return File.OpenRead(pathFS);
            } catch {
            }

#if DEBUG
            try {
                string dir = Path.GetFullPath(Path.Combine("..", "..", "..", "Content"));
                string pathFS = Path.GetFullPath(Path.Combine(dir, path));
                if (pathFS.StartsWith(dir) && File.Exists(pathFS))
                    return File.OpenRead(pathFS);
            } catch {
            }
#endif

            return typeof(CelesteNetServer).Assembly.GetManifestResourceStream("Celeste.Mod.CelesteNet.Server.Content." + path.Replace("/", "."));
        }


        #region Handlers



        #endregion

    }
}
