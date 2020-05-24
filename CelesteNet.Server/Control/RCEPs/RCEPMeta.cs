using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public static partial class RCEndpoints {

        [RCEndpoint("/", null, null, "Root", "Main page.")]
        public static void Root(Frontend f, HttpRequestEventArgs c) {
            f.RespondContent(c, "frontend/main.html");
        }

        [RCEndpoint("/auth", null, null, "Authenticate", "Basic POST authentication endpoint.")]
        public static void Auth(Frontend f, HttpRequestEventArgs c) {
            string key = c.Request.Cookies["celestenet-session"]?.Value;
            string pass;

            try {
                using (StreamReader sr = new StreamReader(c.Request.InputStream, Encoding.UTF8, false, 1024, true))
                using (JsonTextReader jtr = new JsonTextReader(sr))
                    pass = f.Serializer.Deserialize<string>(jtr);
            } catch (Exception e) {
                Logger.Log(LogLevel.DEV, "frontend-auth", e.ToString());
                pass = null;
            }

            if (string.IsNullOrEmpty(pass) && !string.IsNullOrEmpty(key)) {
                if (f.CurrentSessionKeys.Contains(key)) {
                    f.RespondJSON(c, new {
                        Key = key,
                        Info = "Resumed previous session based on cookies."
                    });
                    return;

                } else {
                    f.RespondJSON(c, new {
                        Error = "Previous session expired."
                    });
                    return;
                }
            }

            if (pass == null) {
                f.RespondJSON(c, new {
                    Error = "Invalid data."
                });
                return;
            }

            if (pass == f.Server.Settings.ControlPassword) {
                key = Guid.NewGuid().ToString();
                f.CurrentSessionKeys.Add(key);
                c.Response.SetCookie(new Cookie("celestenet-session", key));
                f.RespondJSON(c, new {
                    Key = key
                });
                return;
            }

            f.RespondJSON(c, new {
                Error = "Incorrect password."
            });
        }

        [RCEndpoint("/ws", null, null, "WebSocket Connection", "Establish a WebSocket control panel connection.")]
        public static void WSPseudo(Frontend f, HttpRequestEventArgs c) {
            f.RespondJSON(c, new {
                Error = "Connect to this endpoint using WebSockets, not plain HTTP."
            });
        }

        [RCEndpoint("/res", "?id={id}", "?id=frontend/css/frontend.css", "Resource", "Obtain a resource.")]
        public static void Resource(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection data = f.ParseQueryString(c.Request.RawUrl);

            string id = data["id"];
            if (string.IsNullOrEmpty(id)) {
                c.Response.StatusCode = (int) HttpStatusCode.NotFound;
                f.RespondJSON(c, new {
                    Error = "No id specified."
                });
                return;
            }

            f.RespondContent(c, id);
        }

        [RCEndpoint("/shutdown", null, null, "Shutdown", "Shut the server down.")]
        public static void Shutdown(Frontend f, HttpRequestEventArgs c) {
            f.RespondJSON(c, "OK");
            f.Server.IsAlive = false;
        }

        [RCEndpoint("/eps", null, null, "Endpoint List", "List of all registered endpoints.")]
        public static void EPs(Frontend f, HttpRequestEventArgs c) {
            f.RespondJSON(c, f.EndPoints);
        }

        [RCEndpoint("/asms", null, null, "Assembly List", "List of all loaded assemblies.")]
        public static void ASMs(Frontend f, HttpRequestEventArgs c) {
            f.RespondJSON(c, AppDomain.CurrentDomain.GetAssemblies().Select(asm => new {
                asm.GetName().Name,
                Version = asm.GetName().Version.ToString()
            }).ToList());
        }

        [RCEndpoint("/status", null, null, "Server Status", "Basic server status information.")]
        public static void Status(Frontend f, HttpRequestEventArgs c) {
            f.RespondJSON(c, new {
                Registered = 32,
                Players = 16,
                Sessions = 4
            });
        }

    }
}
