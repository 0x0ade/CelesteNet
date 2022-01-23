using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Newtonsoft.Json;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

#if NETCORE
using System.Runtime.Loader;
#endif

namespace Celeste.Mod.CelesteNet.Server.Control {
    public static partial class RCEndpoints {

        [RCEndpoint(false, "/deauth", "", "", "Deauthenticate", "Deauthenticate and force-unset all set cookies.")]
        public static void Deauth(Frontend f, HttpRequestEventArgs c) {
            c.Response.SetCookie(new(Frontend.COOKIE_SESSION, "", "/"));
            c.Response.SetCookie(new(COOKIE_KEY, "", "/"));
            c.Response.SetCookie(new(COOKIE_DISCORDAUTH, "", "/"));
            f.RespondJSON(c, new {
                Info = "Success."
            });
        }

        [RCEndpoint(false, "/res", "?id={id}", "?id=frontend/cp/css/frontend.css", "Resource", "Obtain a resource.")]
        public static void Resource(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            string? id = args["id"];
            if (id.IsNullOrEmpty()) {
                c.Response.StatusCode = (int) HttpStatusCode.NotFound;
                f.RespondJSON(c, new {
                    Error = "No id specified."
                });
                return;
            }

            f.RespondContent(c, id);
        }

    }
}
