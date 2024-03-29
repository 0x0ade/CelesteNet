﻿using System.Collections.Specialized;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public static partial class RCEndpoints {

        [RCEndpoint(false, "/deauth", "", "", "Deauthenticate", "Deauthenticate and force-unset all set cookies.")]
        public static void Deauth(Frontend f, HttpRequestEventArgs c) {
            f.UnsetAllCookies(c);
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
