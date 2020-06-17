#if NETCORE
using Microsoft.AspNetCore.StaticFiles;
#endif
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using WebSocketSharp.Server;
using Celeste.Mod.Helpers;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class Frontend : CelesteNetServerModule<FrontendSettings> {

        public static readonly string COOKIE_SESSION = "celestenet-session";

        public readonly List<RCEndpoint> EndPoints = new List<RCEndpoint>();
        public readonly HashSet<string> CurrentSessionKeys = new HashSet<string>();

        private HttpServer? HTTPServer;
        private WebSocketServiceHost? WSHost;

#if NETCORE
        private readonly FileExtensionContentTypeProvider ContentTypeProvider = new FileExtensionContentTypeProvider();
#endif

        public JsonSerializer Serializer = new JsonSerializer() {
            Formatting = Formatting.Indented
        };

        public override void Init(CelesteNetServerModuleWrapper wrapper) {
            base.Init(wrapper);

            Logger.Log(LogLevel.VVV, "frontend", "Scanning for endpoints");
            foreach (Type t in CelesteNetUtils.GetTypes()) {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)) {
                    foreach (RCEndpointAttribute epa in m.GetCustomAttributes(typeof(RCEndpointAttribute), false)) {
                        RCEndpoint ep = epa.Data;
                        Logger.Log(LogLevel.VVV, "frontend", $"Found endpoint: {ep.Path} - {ep.Name} ({m.Name}::{t.FullName})");
                        ep.Handle = (f, c) => m.Invoke(null, new object[] { f, c });
                        EndPoints.Add(ep);
                    }
                }
            }

            if (Server == null)
                return;

            Server.OnConnect += OnConnect;
            Server.OnSessionStart += OnSessionStart;
            Server.OnDisconnect += OnDisconnect;

            ChatModule chat = Server.Get<ChatModule>();
            chat.OnReceive += OnChatReceive;
            chat.OnForceSend += OnForceSend;
        }

        public override void Start() {
            if (Server == null)
                return;

            base.Start();

            Logger.Log(LogLevel.INF, "frontend", $"Startup on port {Settings.Port}");

            HTTPServer = new HttpServer(Settings.Port);

            HTTPServer.OnGet += HandleRequestRaw;
            HTTPServer.OnPost += HandleRequestRaw;

            HTTPServer.WebSocketServices.AddService<FrontendWebSocket>("/ws", ws => ws.Frontend = this);

            HTTPServer.Start();

            HTTPServer.WebSocketServices.TryGetServiceHost("/ws", out WSHost);
        }

        public override void Dispose() {
            base.Dispose();

            Logger.Log(LogLevel.INF, "frontend", "Shutdown");

            try {
                HTTPServer?.Stop();
            } catch (Exception) {
            }
            HTTPServer = null;

            if (Server == null)
                return;

            Server.OnConnect -= OnConnect;
            Server.OnSessionStart -= OnSessionStart;
            lock (Server.Connections)
                foreach (CelesteNetPlayerSession session in Server.PlayersByCon.Values)
                    session.OnEnd -= OnSessionEnd;
            Server.OnDisconnect -= OnDisconnect;

            if (Server.TryGet(out ChatModule? chat)) {
                chat.OnReceive -= OnChatReceive;
                chat.OnForceSend -= OnForceSend;
            }
        }

        private void OnConnect(CelesteNetServer server, CelesteNetConnection con) {
            BroadcastCMD("update", "/status");
        }

        private void OnSessionStart(CelesteNetPlayerSession session) {
            BroadcastCMD("update", "/status");
            BroadcastCMD("update", "/players");
            session.OnEnd += OnSessionEnd;
        }

        private void OnSessionEnd(CelesteNetPlayerSession session, DataPlayerInfo? lastPlayerInfo) {
            BroadcastCMD("update", "/status");
            BroadcastCMD("update", "/players");
        }

        private void OnDisconnect(CelesteNetServer server, CelesteNetConnection con, CelesteNetPlayerSession? session) {
            if (session == null)
                BroadcastCMD("update", "/status");
        }

        private bool OnChatReceive(ChatModule chat, DataChat msg) {
            BroadcastCMD("chat", msg.ToFrontendChat());
            return true;
        }

        private void OnForceSend(ChatModule chat, DataChat msg) {
            BroadcastCMD("chat", msg.ToFrontendChat());
        }

        public Stream? OpenContent(string path) {
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

            try {
                string dir = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "CelesteNet.Server.FrontendModule", "Content"));
                string pathFS = Path.GetFullPath(Path.Combine(dir, path));
                if (pathFS.StartsWith(dir) && File.Exists(pathFS))
                    return File.OpenRead(pathFS);
            } catch {
            }
#endif

            return typeof(CelesteNetServer).Assembly.GetManifestResourceStream("Celeste.Mod.CelesteNet.Server.Content." + path.Replace("/", "."));
        }

        private void HandleRequestRaw(object? sender, HttpRequestEventArgs c) {
            try {
                using (c.Request.InputStream)
                using (c.Response) {
                    HandleRequest(c);
                }

            } catch (Exception e) {
                Logger.Log(LogLevel.ERR, "frontend", $"Frontend failed responding: {e}");
            }
        }

        private void HandleRequest(HttpRequestEventArgs c) {
            Logger.Log(LogLevel.VVV, "frontend", $"{c.Request.RemoteEndPoint} requested: {c.Request.RawUrl}");

            string url = c.Request.RawUrl;
            int indexOfSplit = url.IndexOf('?');
            if (indexOfSplit != -1)
                url = url.Substring(0, indexOfSplit);

            RCEndpoint endpoint =
                EndPoints.FirstOrDefault(ep => ep.Path == c.Request.RawUrl) ??
                EndPoints.FirstOrDefault(ep => ep.Path == url) ??
                EndPoints.FirstOrDefault(ep => ep.Path.ToLowerInvariant() == c.Request.RawUrl.ToLowerInvariant()) ??
                EndPoints.FirstOrDefault(ep => ep.Path.ToLowerInvariant() == url.ToLowerInvariant());

            if (endpoint == null) {
                RespondContent(c, "frontend/" + url.Substring(1));
                return;
            }

            if (endpoint.Auth && !IsAuthorized(c)) {
                RespondJSON(c, new {
                    Error = "Unauthorized."
                });
            }

            endpoint.Handle(this, c);
        }

        public bool IsAuthorized(HttpRequestEventArgs c)
            => c.Request.Cookies[COOKIE_SESSION]?.Value is string session && CurrentSessionKeys.Contains(session);

        public void BroadcastRawString(string data) {
            WSHost?.Sessions.Broadcast(data);
        }

        public void BroadcastRawObject(object obj) {
            if (WSHost == null)
                return;

            using (MemoryStream ms = new MemoryStream()) {
                using (StreamWriter sw = new StreamWriter(ms, Encoding.UTF8, 1024, true))
                using (JsonTextWriter jtw = new JsonTextWriter(sw))
                    Serializer.Serialize(jtw, obj);

                ms.Seek(0, SeekOrigin.Begin);

                using (StreamReader sr = new StreamReader(ms, Encoding.UTF8, false, 1024, true))
                    WSHost.Sessions.Broadcast(sr.ReadToEnd());
            }
        }

        public void BroadcastCMD(string id, object obj) {
            BroadcastRawString("cmd");
            BroadcastRawString(id);
            BroadcastRawObject(obj);
        }

        #region Read / Parse Helpers

        public NameValueCollection ParseQueryString(string url) {
            NameValueCollection nvc = new NameValueCollection();

            int indexOfSplit = url.IndexOf('?');
            if (indexOfSplit == -1)
                return nvc;
            url = url.Substring(indexOfSplit + 1);

            string[] args = url.Split('&');
            foreach (string arg in args) {
                indexOfSplit = arg.IndexOf('=');
                if (indexOfSplit == -1)
                    continue;
                nvc[arg.Substring(0, indexOfSplit)] = arg.Substring(indexOfSplit + 1);
            }

            return nvc;
        }

        #endregion

        #region Write Helpers

        public void RespondContent(HttpRequestEventArgs c, string id) {
            using (MemoryStream ms = new MemoryStream())
            using (Stream? s = OpenContent(id)) {
                if (s == null) {
                    c.Response.StatusCode = (int) HttpStatusCode.NotFound;
                    RespondJSON(c, new {
                        Error = "Resource not found."
                    });
                    return;
                }

                s.CopyTo(ms);

                ms.Seek(0, SeekOrigin.Begin);

#if NETCORE
                if (ContentTypeProvider.TryGetContentType(id, out string contentType))
                    c.Response.ContentType = contentType;
#else
                c.Response.ContentType = MimeMapping.GetMimeMapping(id);
#endif

                Respond(c, ms.ToArray());
            }
        }

        public void RespondJSON(HttpRequestEventArgs c, object obj) {
            using (MemoryStream ms = new MemoryStream()) {
                using (StreamWriter sw = new StreamWriter(ms, Encoding.UTF8, 1024, true))
                using (JsonTextWriter jtw = new JsonTextWriter(sw))
                    Serializer.Serialize(jtw, obj);

                ms.Seek(0, SeekOrigin.Begin);

                c.Response.ContentType = "application/json";
                Respond(c, ms.ToArray());
            }
        }

        public void Respond(HttpRequestEventArgs c, string str) {
            Respond(c, Encoding.UTF8.GetBytes(str));
        }

        public void Respond(HttpRequestEventArgs c, byte[] buf) {
            c.Response.ContentLength64 = buf.Length;
            c.Response.Close(buf, true);
        }

        #endregion

    }
}
