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
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public static partial class RCEndpoints {

        [RCEndpoint("/", null, null, "Root", "Main page.")]
        public static void Root(Frontend f, HttpRequestEventArgs c) {
            f.RespondContent(c, "frontend/main.html");
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

    }
}
