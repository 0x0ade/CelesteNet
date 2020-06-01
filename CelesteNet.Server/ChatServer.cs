using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Control;
using Microsoft.Xna.Framework;
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

        public readonly Dictionary<uint, DataChat> ChatLog = new Dictionary<uint, DataChat>();
        public uint NextID;

        public ChatServer(CelesteNetServer server) {
            Server = server;
            Server.Data.RegisterHandlersIn(this);
        }

        public void Start() {
            Logger.Log(LogLevel.INF, "chat", "Startup");
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "chat", "Shutdown");
        }


        public void Handle(CelesteNetConnection con, DataChat msg) {
            if (!msg.CreatedByServer) {
                msg.Text.Replace("\r", "").Replace("\n", "");
                if (msg.Text.Length > Server.Settings.MaxChatTextLength)
                    msg.Text = msg.Text.Substring(0, Server.Settings.MaxChatTextLength);

                msg.Tag = "";
                msg.Color = Color.White;
            }

            if (msg.Text.Length == 0)
                return;

            lock (ChatLog) {
                ChatLog[msg.ID = NextID++] = msg;
            }

            msg.Date = DateTime.UtcNow;

            Server.Control.BroadcastCMD("chat", new {
                Color = msg.Color.ToHex(),
                Text = msg.ToString()
            });

            if (msg.Text.StartsWith(Server.Settings.CommandPrefix)) {
                // TODO: Handle commands separately!
                return;
            }

            // TODO: BROADCAST!
        }


        public void Broadcast(string text) {
            Logger.Log(LogLevel.INF, "chat", $"Broadcasting: {text}");
            lock (ChatLog) {
                Handle(null, new DataChat() {
                    Text = text,
                    Color = Server.Settings.ColorBroadcast
                });
            }
        }

    }
}
