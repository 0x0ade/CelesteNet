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

        [RCEndpoint(false, "/auth", null, null, "Authenticate", "Basic POST authentication endpoint.")]
        public static void Auth(Frontend f, HttpRequestEventArgs c) {
            string? key = c.Request.Cookies[Frontend.COOKIE_SESSION]?.Value;
            string? pass;

            try {
                using (StreamReader sr = new StreamReader(c.Request.InputStream, Encoding.UTF8, false, 1024, true))
                using (JsonTextReader jtr = new JsonTextReader(sr))
                    pass = f.Serializer.Deserialize<string>(jtr);
            } catch (Exception e) {
                Logger.Log(LogLevel.DEV, "frontend-auth", e.ToString());
                pass = null;
            }

            if (pass.IsNullOrEmpty() && !key.IsNullOrEmpty()) {
                if (f.CurrentSessionKeys.Contains(key)) {
                    f.RespondJSON(c, new {
                        Key = key,
                        Info = "Resumed previous session based on cookies."
                    });
                    return;

                } else {
                    c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                    f.RespondJSON(c, new {
                        Error = "Previous session expired."
                    });
                    return;
                }
            }

            if (pass == null) {
                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                f.RespondJSON(c, new {
                    Error = "Invalid data."
                });
                return;
            }

            if (pass == f.Settings.Password) {
                key = Guid.NewGuid().ToString();
                f.CurrentSessionKeys.Add(key);
                c.Response.SetCookie(new Cookie(Frontend.COOKIE_SESSION, key));
                f.RespondJSON(c, new {
                    Key = key
                });
                return;
            }

            c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
            f.RespondJSON(c, new {
                Error = "Incorrect password."
            });
        }

        [RCEndpoint(false, "/ws", null, null, "WebSocket Connection", "Establish a WebSocket control panel connection.")]
        public static void WSPseudo(Frontend f, HttpRequestEventArgs c) {
            c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
            f.RespondJSON(c, new {
                Error = "Connect to this endpoint using WebSockets, not plain HTTP."
            });
        }

        [RCEndpoint(true, "/shutdown", null, null, "Shutdown", "Shut the server down.")]
        public static void Shutdown(Frontend f, HttpRequestEventArgs c) {
            DateTime start = DateTime.UtcNow;

            lock (f.Server.Connections) {
                foreach (CelesteNetConnection con in f.Server.Connections) {
                    con.Send(new DataDisconnectReason { Text = "Server shutting down" });
                    con.Send(new DataInternalDisconnect());
                }
            }

            // This isn't perf critical and would require a heavily specialized event anyway.
            bool timeout;
            while (f.Server.Connections.Count > 0 || (timeout = (DateTime.UtcNow - start).TotalSeconds >= 3))
                Thread.Sleep(100);

            f.RespondJSON(c, new {
                Info = "OK",
                Timeout = timeout
            });
            f.Server.IsAlive = false;
        }

        [RCEndpoint(false, "/eps", null, null, "Endpoint List", "List of all registered endpoints.")]
        public static void EPs(Frontend f, HttpRequestEventArgs c) {
            f.RespondJSON(c, f.EndPoints);
        }

        [RCEndpoint(true, "/asms", null, null, "Assembly List", "List of all loaded assemblies.")]
        public static void ASMs(Frontend f, HttpRequestEventArgs c) {
            f.RespondJSON(c, AppDomain.CurrentDomain.GetAssemblies().Select(asm => new {
                asm.GetName().Name,
                Version = asm.GetName().Version?.ToString() ?? "",
                Context =
#if NETCORE
                    (AssemblyLoadContext.GetLoadContext(asm) ?? AssemblyLoadContext.Default)?.Name ?? "Unknown",
#else
                    AppDomain.CurrentDomain.FriendlyName
#endif
            }).ToList());
        }

        [RCEndpoint(false, "/status", null, null, "Server Status", "Basic server status information.")]
        public static void Status(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);
            f.RespondJSON(c, new {
                StartupTime = f.Server.StartupTime.ToUnixTime(),
                GCMemory = GC.GetTotalMemory(false),
                Modules = f.Server.Modules.Count,
                f.Server.PlayerCounter,
                Registered = f.Server.UserData.GetRegisteredCount(),
                Banned = f.Server.UserData.LoadAll<BanInfo>().Count(ban => !ban.Reason.IsNullOrEmpty()),
                Connections = auth ? f.Server.Connections.Count : (int?) null,
                PlayersByCon = auth ? f.Server.PlayersByCon.Count : (int?) null,
                PlayersByID = auth ? f.Server.PlayersByID.Count : (int?) null,
                PlayerRefs = f.Server.Data.GetRefs<DataPlayerInfo>().Length
            });
        }

        [RCEndpoint(true, "/userinfos", "", "", "User Infos", "Get some basic information about ALL users.")]
        public static void UserInfos(Frontend f, HttpRequestEventArgs c) {
            f.RespondJSON(c, f.Server.UserData.GetAll().Select(uid => {
                BasicUserInfo info = f.Server.UserData.Load<BasicUserInfo>(uid);
                BanInfo ban = f.Server.UserData.Load<BanInfo>(uid);
                return new {
                    UID = uid,
                    info.Name,
                    info.Discrim,
                    info.Tags,
                    Key = f.Server.UserData.GetKey(uid),
                    Ban = ban.Reason.IsNullOrEmpty() ? null : new {
                        ban.Reason,
                        From = ban.From?.ToUnixTime() ?? 0,
                        To = ban.To?.ToUnixTime() ?? 0
                    }
                };
            }).ToArray());
        }

        [RCEndpoint(false, "/players", null, null, "Player List", "Basic player list.")]
        public static void Players(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);
            f.RespondJSON(c, f.Server.PlayersByID.Values.Select(p => new {
                p.ID,
                UID = auth ? p.UID : null,
                p.PlayerInfo?.Name,
                p.PlayerInfo?.FullName,
                p.PlayerInfo?.DisplayName,
                Connection = auth ? p.Con.ID : null,
                ConnectionUID = auth ? p.ConUID : null,
            }).ToArray());
        }

        [RCEndpoint(false, "/channels", null, null, "Channel List", "Basic channel list.")]
        public static void Channels(Frontend f, HttpRequestEventArgs c) {
            IEnumerable<Channel> channels = f.Server.Channels.All;
            if (!f.IsAuthorized(c))
                channels = channels.Where(c => !c.IsPrivate);
            f.RespondJSON(c, channels.Select(c => new {
                c.ID, c.Name, c.IsPrivate,
                Players = c.Players.Select(p => p.ID).ToArray()
            }).ToArray());
        }

        [RCEndpoint(false, "/chatlog", "?count={count}&detailed={true|false}", "?count=20&detailed=false", "Chat Log", "Basic chat log.")]
        public static void ChatLog(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            if (!int.TryParse(args["count"], out int count) || count <= 0)
                count = 20;
            if (!auth && count > 100)
                count = 100;

            if (!bool.TryParse(args["detailed"], out bool detailed))
                detailed = false;

            ChatModule chat = f.Server.Get<ChatModule>();
            List<object> log = new List<object>();
            RingBuffer<DataChat> buffer = chat.ChatBuffer;
            for (int i = Math.Max(-chat.ChatBuffer.Moved, -count); i < 0; i++) {
                DataChat msg = buffer[i];
                if (msg.Target == null || auth)
                    log.Add(detailed ? msg.ToDetailedFrontendChat() : msg.ToFrontendChat());
            }

            f.RespondJSON(c, log);
        }

        [RCEndpoint(true, "/settings", "?module={id}", "?module=CelesteNet.Server", "Server Settings", "Get the settings of any server module as YAML.")]
        public static void Settings(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);
            string moduleID = args["module"];
            if (moduleID.IsNullOrEmpty()) {
                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                f.RespondJSON(c, new {
                    Error = "No ID."
                });
                return;
            }

            CelesteNetServerModuleSettings? settings;
            if (moduleID == "CelesteNet.Server") {
                settings = f.Server.Settings;
            } else {
                lock (f.Server.Modules)
                    settings = f.Server.Modules.FirstOrDefault(m => m.Wrapper.ID == moduleID)?.GetSettings();
            }

            if (settings == null) {
                c.Response.StatusCode = (int) HttpStatusCode.NotFound;
                f.RespondJSON(c, new {
                    Error = $"Module {moduleID} not loaded or doesn't have settings."
                });
                return;
            }

            if (c.Request.HttpMethod == "POST") {
                try {
                    using (StreamReader sr = new StreamReader(c.Request.InputStream, Encoding.UTF8, false, 1024, true))
                        settings.Load(sr);
                    settings.Save();
                    f.RespondJSON(c, new {
                        Info = "Success."
                    });
                    return;
                } catch (Exception e) {
                    f.RespondJSON(c, new {
                        Error = e.ToString()
                    });
                    return;
                }
            }

            StringBuilder sb = new StringBuilder();
            using (StringWriter sw = new StringWriter(sb))
                settings.Save(sw);
            f.Respond(c, sb.ToString());
        }

    }
}
