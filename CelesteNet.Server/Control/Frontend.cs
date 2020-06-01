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

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class Frontend : IDisposable {

        public static readonly string COOKIE_SESSION = "celestenet-session";

        public readonly CelesteNetServer Server;
        public readonly List<RCEndpoint> EndPoints = new List<RCEndpoint>();
        public readonly HashSet<string> CurrentSessionKeys = new HashSet<string>();

        private HttpServer HTTPServer;
        private WebSocketServiceHost WSHost;

#if NETCORE
        private FileExtensionContentTypeProvider ContentTypeProvider = new FileExtensionContentTypeProvider();
#endif

        public JsonSerializer Serializer = new JsonSerializer() {
            Formatting = Formatting.Indented
        };

        public Frontend(CelesteNetServer server) {
            Server = server;

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
        }

        public void Start() {
            Logger.Log(LogLevel.INF, "frontend", $"Startup on port {Server.Settings.ControlPort}");

            HTTPServer = new HttpServer(Server.Settings.ControlPort);

            HTTPServer.OnGet += HandleRequestRaw;
            HTTPServer.OnPost += HandleRequestRaw;

            HTTPServer.WebSocketServices.AddService<FrontendWebSocket>("/ws", ws => ws.Frontend = this);

            HTTPServer.Start();

            HTTPServer.WebSocketServices.TryGetServiceHost("/ws", out WSHost);
        }

        private void HandleRequestRaw(object sender, HttpRequestEventArgs c) {
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

            if (endpoint.Auth && !CurrentSessionKeys.Contains(c.Request.Cookies[Frontend.COOKIE_SESSION]?.Value)) {
                RespondJSON(c, new {
                    Error = "Unauthorized."
                });
            }

            endpoint.Handle(this, c);
        }

        public void Dispose() {
            Logger.Log(LogLevel.INF, "frontend", "Shutdown");

            HTTPServer?.Stop();
            HTTPServer = null;
        }

        public void BroadcastRawString(string data) {
            WSHost.Sessions.Broadcast(data);
        }

        public void BroadcastRawObject(object obj) {
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
            using (Stream s = Server.OpenContent(id)) {
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
