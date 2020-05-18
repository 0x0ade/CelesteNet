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
