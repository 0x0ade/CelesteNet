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
        public readonly RingBuffer<DataChat> ChatBuffer = new RingBuffer<DataChat>(3000);
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


        public DataChat PrepareAndLog(CelesteNetConnection from, DataChat msg) {
            if (!msg.CreatedByServer) {
                msg.Player = Server.GetPlayerInfo(from);
                if (msg.Player == null)
                    return null;

                msg.Text = msg.Text?.Trim();
                if (string.IsNullOrEmpty(msg.Text))
                    return null;

                msg.Text.Replace("\r", "").Replace("\n", "");
                if (msg.Text.Length > Server.Settings.MaxChatTextLength)
                    msg.Text = msg.Text.Substring(0, Server.Settings.MaxChatTextLength);

                msg.Tag = "";
                msg.Color = Color.White;
            }

            if (msg.Text.Length == 0)
                return null;

            lock (ChatLog) {
                ChatLog[msg.ID = NextID++] = msg;
                ChatBuffer.Set(msg).Move(1);
            }

            msg.Date = DateTime.UtcNow;

            if (!msg.CreatedByServer)
                Logger.Log(LogLevel.INF, "chatmsg", msg.ToString());
            Server.Control.BroadcastCMD("chat", msg.ToFrontendChat());
            return msg;
        }


        public void Handle(CelesteNetConnection con, DataChat msg) {
            PrepareAndLog(con, msg);

            if (msg.Text.StartsWith(Server.Settings.CommandPrefix)) {
                // TODO: Handle commands separately!
                return;
            }

            Server.Broadcast(msg);
        }


        public void Broadcast(string text, string tag = null, Color? color = null) {
            Logger.Log(LogLevel.INF, "chat", $"Broadcasting: {text}");
            lock (ChatLog) {
                Handle(null, new DataChat() {
                    Text = text,
                    Tag = tag,
                    Color = color ?? Server.Settings.ColorBroadcast
                });
            }
        }

        public void Send(CelesteNetPlayerSession player, string text, string tag = null, Color? color = null) {
            Logger.Log(LogLevel.INF, "chat", $"Sending to {player.PlayerInfo}: {text}");

            player.Con.Send(PrepareAndLog(null, new DataChat() {
                Target = player.PlayerInfo,
                Text = text,
                Tag = tag,
                Color = color ?? Server.Settings.ColorServer
            }));
        }

        public void Resend(DataChat msg) {
            Logger.Log(LogLevel.INF, "chatupd", msg.ToString());
            Server.Control.BroadcastCMD("chat", msg.ToFrontendChat());
            if (msg.Target == null) {
                Server.Broadcast(msg);
                return;
            }

            CelesteNetPlayerSession player;
            lock (Server.Connections)
                if (!Server.PlayersByID.TryGetValue(msg.Target.ID, out player))
                    return;
            player.Con?.Send(msg);
        }

    }
}
