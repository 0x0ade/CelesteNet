using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Newtonsoft.Json;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

#if NETCORE
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
#endif

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public static partial class RCEndpoints {

        [RCEndpoint(false, "/auth", null, null, "Authenticate", "Basic POST authentication endpoint.")]
        public static void Auth(Frontend f, HttpRequestEventArgs c) {
            string? key = f.TryGetSessionAuthCookie(c);
            string? pass;

            try {
                using StreamReader sr = new(c.Request.InputStream, Encoding.UTF8, false, 1024, true);
                using JsonTextReader jtr = new(sr);
                pass = f.Serializer.Deserialize<string>(jtr);
            } catch (Exception e) {
                Logger.Log(LogLevel.DEV, "frontend-auth", e.ToString());
                pass = null;
            }

            bool expired = false;

            if (pass.IsNullOrEmpty() && !key.IsNullOrEmpty()) {
                if (f.CurrentSessionKeys.Contains(key)) {
                    f.RespondJSON(c, new {
                        Key = key,
                        Info = "Resumed previous session based on cookies."
                    });
                    return;

                } else {
                    expired = true;
                }
            }

            key = f.TryGetKeyCookie(c);
            string sessionkey;
            if (!key.IsNullOrEmpty() &&
                f.Server.UserData.GetUID(key) is string uid && !uid.IsNullOrEmpty() &&
                f.Server.UserData.TryLoad(uid, out BasicUserInfo info)) {
                    sessionkey = "";
                    if (info.Tags.Contains(BasicUserInfo.TAG_AUTH_EXEC)) {
                        sessionkey = f.GetNewKey(execAuth: true);
                    } else if (info.Tags.Contains(BasicUserInfo.TAG_AUTH)) {
                        sessionkey = f.GetNewKey();
                    } 
                    if (!sessionkey.IsNullOrEmpty()) {
                        f.SetSessionAuthCookie(c, sessionkey);
                        f.RespondJSON(c, new {
                            Key = sessionkey,
                            Info = string.IsNullOrEmpty(info.Discrim) || info.Discrim == "0"
                            ? $"Welcome, {info.Name} ({uid})"
                            : $"Welcome, {info.Name}#{info.Discrim}"
                        });
                        return;
                    } else {
                        // Fall through to "previous session" / password checks.
                    }
            }

            if (expired) {
                c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                f.RespondJSON(c, new {
                    Error = "Previous session expired."
                });
                return;
            }

            if (pass == null) {
                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                f.RespondJSON(c, new {
                    Error = "Invalid data."
                });
                return;
            }

            sessionkey = "";
            if (pass == f.Settings.PasswordExec) {
                sessionkey = f.GetNewKey(execAuth: true);
            } else if (pass == f.Settings.Password) {
                sessionkey = f.GetNewKey();
            }

            if (!sessionkey.IsNullOrEmpty()) {
                f.SetSessionAuthCookie(c, sessionkey);
                f.RespondJSON(c, new {
                    Key = sessionkey
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

            using (f.Server.ConLock.R())
                foreach (CelesteNetConnection con in f.Server.Connections) {
                    con.Send(new DataDisconnectReason { Text = "Server shutting down" });
                    con.Send(new DataInternalDisconnect());
                }

            // This isn't perf critical and would require a heavily specialized event anyway.
            bool timeout;
            while ((timeout = (DateTime.UtcNow - start).TotalSeconds >= 3) || f.Server.Connections.Count > 0)
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

        private static float NetPlusPoolActvRate;
        private static object[]? NetPlusThreadStats;
        private static object[]? NetPlusRoleStats;

        private struct BandwidthRate {

            public float GlobalRate, MinConRate, MaxConRate, AvgConRate;

            public void UpdateRate(CelesteNetServer server, Func<CelesteNetConnection, float?> cb) {
                GlobalRate = 0;
                MinConRate = float.MaxValue;
                MaxConRate = float.MinValue;
                AvgConRate = 0;
                int numCons = 0;
                foreach (CelesteNetConnection con in server.Connections) {
                    float? r = cb(con);
                    if (r == null)
                        continue;
                    float conRate = r.Value;

                    GlobalRate += conRate;
                    if (conRate < MinConRate)
                        MinConRate = conRate;
                    if (conRate > MaxConRate)
                        MaxConRate = conRate;
                    AvgConRate += conRate;
                    numCons++;
                }
                if (numCons > 0)
                    AvgConRate /= numCons;
                else
                    MinConRate = MaxConRate = AvgConRate = 0;
            }

        }

        private static readonly object StatLock = new();
        private static int NumCons, NumTCPCons, NumUDPCons;
        private static BandwidthRate TCPRecvBpSRate, TCPRecvPpSRate, TCPSendBpSRate, TCPSendPpSRate;
        private static BandwidthRate UDPRecvBpSRate, UDPRecvPpSRate, UDPSendBpSRate, UDPSendPpSRate;

        public static void UpdateStats(CelesteNetServer server) {
            lock (StatLock) {
                // Update connection stats
                NumCons = 0;
                NumTCPCons = 0;
                NumUDPCons = 0;
                using (server.ConLock.R())
                    foreach (CelesteNetConnection con in server.Connections) {
                        NumCons++;
                        if (con is CelesteNetTCPUDPConnection tcpUdpCon) {
                            NumTCPCons++;
                            if (tcpUdpCon.UDPConnectionID >= 0)
                                NumUDPCons++;
                        }
                    }

                // Update bandwidth stats
                using (server.ConLock.R()) {
                    TCPRecvBpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.TCPRecvRate.ByteRate : null);
                    TCPRecvPpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.TCPRecvRate.PacketRate : null);
                    TCPSendBpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.TCPSendRate.ByteRate : null);
                    TCPSendPpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.TCPSendRate.PacketRate : null);
                    UDPRecvBpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.UDPRecvRate.ByteRate : null);
                    UDPRecvPpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.UDPRecvRate.PacketRate : null);
                    UDPSendBpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.UDPSendRate.ByteRate : null);
                    UDPSendPpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.UDPSendRate.PacketRate : null);
                }

                // Update NetPlus stats
                using (server.ThreadPool.PoolLock.R())
                using (server.ThreadPool.RoleLock.R())
                using (server.ThreadPool.Scheduler.RoleLock.R()) {
                    NetPlusPoolActvRate = server.ThreadPool.ActivityRate;
                    NetPlusThreadStats = server.ThreadPool.EnumerateThreads().Select(t => new {
                        t.ActivityRate,
                        Role = t.Role.ToString()
                    }).ToArray();
                    NetPlusRoleStats = server.ThreadPool.Scheduler.EnumerateRoles().Select(r => new {
                        r.ActivityRate,
                        Role = r.ToString(),
                        NumThreads = server.ThreadPool.EnumerateThreads().Count(t => t.Role == r)
                    }).ToArray();
                }
            }
        }

        [RCEndpoint(false, "/status", null, null, "Server Status", "Basic server status information.")]
        public static void Status(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);
            lock (StatLock)
                f.RespondJSON(c, new {
                    StartupTime = f.Server.StartupTime.ToUnixTimeMillis(),
                    GCMemory = GC.GetTotalMemory(false),
                    Modules = f.Server.Modules.Count,
                    TickRate = f.Server.CurrentTickRate,

                    f.Server.PlayerCounter,
                    Registered = f.Server.UserData.GetRegisteredCount(),
                    Banned = f.Server.UserData.LoadAll<BanInfo>().GroupBy(ban => ban.UID).Select(g => g.First()).Count(ban => !ban.Reason.IsNullOrEmpty()),

                    Connections = auth ? NumCons : (int?) null,
                    TCPConnections = auth ? NumTCPCons : (int?) null,
                    UDPConnections = auth ? NumUDPCons : (int?) null,
                    Sessions = auth ? f.Server.Sessions.Count : (int?) null,
                    PlayersByCon = auth ? f.Server.PlayersByCon.Count : (int?) null,
                    PlayersByID = auth ? f.Server.PlayersByID.Count : (int?) null,
                    PlayerRefs = f.Server.Data.GetRefs<DataPlayerInfo>().Length,

                    TCPDownlinkBpS = TCPRecvBpSRate, TCPDownlinkPpS = TCPRecvPpSRate,
                    UDPDownlinkBpS = UDPRecvBpSRate, UDPDownlinkPpS = UDPRecvPpSRate,
                    TCPUplinkBpS = TCPSendBpSRate, TCPUplinkPpS = TCPSendPpSRate,
                    UDPUplinkBpS = UDPSendBpSRate, UDPUplinkPpS = UDPSendPpSRate,
                });
        }


        [RCEndpoint(false, "/netplus", null, null, "NetPlus Status", "Status information about NetPlus")]
        public static void NetPlus(Frontend f, HttpRequestEventArgs c) {
            lock (StatLock)
                f.RespondJSON(c, new {
                    PoolActivityRate = NetPlusPoolActvRate,
                    PoolNumThreads = f.Server.ThreadPool.NumThreads,
                    PoolThreads = NetPlusThreadStats,
                    PoolRoles = NetPlusRoleStats,

                    SchedulerExecDuration = f.Server.ThreadPool.Scheduler.LastSchedulerExecDuration,
                    SchedulerNumThreadsReassigned = f.Server.ThreadPool.Scheduler.LastSchedulerExecNumThreadsReassigned,
                    SchedulerNumThreadsIdled = f.Server.ThreadPool.Scheduler.LastSchedulerExecNumThreadsIdeling
                });
        }

        [RCEndpoint(true, "/userinfos", "?from={first}&count={count}", "?from=0&count=100", "User Infos", "Get some basic information about ALL users.")]
        public static void UserInfos(Frontend f, HttpRequestEventArgs c) {
            using UserDataBatchContext ctx = f.Server.UserData.OpenBatch();

            string[] uids = f.Server.UserData.GetAll();

            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);
            if (!int.TryParse(args["from"], out int from) || from <= 0)
                from = 0;
            if (!int.TryParse(args["count"], out int count) || count <= 0)
                count = 100;
            if (from + count > uids.Length)
                count = uids.Length - from;

            f.RespondJSON(c, uids.Skip(from).Take(count).Select(uid => {
                BasicUserInfo info = f.Server.UserData.Load<BasicUserInfo>(uid);
                BanInfo ban = f.Server.UserData.Load<BanInfo>(uid);
                KickHistory kicks = f.Server.UserData.Load<KickHistory>(uid);
                return new {
                    UID = uid,
                    info.Name,
                    info.Discrim,
                    info.Tags,
                    Key = (!f.IsAuthorizedExec(c) && info.Tags.Contains(BasicUserInfo.TAG_AUTH)) || info.Tags.Contains(BasicUserInfo.TAG_AUTH_EXEC) ? null : f.Server.UserData.GetKey(uid),
                    Ban = ban.Reason.IsNullOrEmpty() ? null : new {
                        ban.Name,
                        ban.Reason,
                        From = ban.From?.ToUnixTimeMillis() ?? 0,
                        To = ban.To?.ToUnixTimeMillis() ?? 0
                    },
                    Kicks = kicks.Log.Select(e => new {
                        e.Reason,
                        From = e.From?.ToUnixTimeMillis() ?? 0
                    }).ToArray()
                };
            }).ToArray());
        }

        [RCEndpoint(false, "/players", null, null, "Player List", "Basic player list.")]
        public static void Players(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);

            // Gate the players endpoint behind control panel auth or player auth, as exposing online player names is a bit eh.
            if (!auth && (
                    f.TryGetKeyCookie(c) is not string key || key.IsNullOrEmpty() ||
                    f.Server.UserData.GetUID(key) is not string uid || uid.IsNullOrEmpty()
            )) {
                c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                f.RespondJSON(c, new {
                    Error = "Unauthorized."
                });
                return;
            }

            object responseObj;
            using (f.Server.ConLock.R()) {
                responseObj = f.Server.PlayersByID.Values.Select(p => f.PlayerSessionToFrontend(p, auth)).ToArray();
            }
            f.RespondJSON(c, responseObj);
        }

        [RCEndpoint(false, "/channels", null, null, "Channel List", "Basic channel list.")]
        public static void Channels(Frontend f, HttpRequestEventArgs c) {
            IEnumerable<Channel> channels = f.Server.Channels.All;
            if (!f.IsAuthorized(c))
                channels = channels.Where(c => !c.IsPrivate);
            f.RespondJSON(c, channels.Select(c => new {
                c.ID, c.Name, c.IsPrivate,
                Players = c.Players.Select(p => p.SessionID).ToArray()
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
            List<object> log = new();
            RingBuffer<DataChat?> buffer = chat.ChatBuffer;
            lock (buffer) {
                for (int i = Math.Max(-buffer.Moved, -count); i < 0; i++) {
                    DataChat? msg = buffer[i];
                    if (msg != null && ((msg.Targets == null) || auth))
                        log.Add(detailed ? msg.ToDetailedFrontendChat() : msg.ToFrontendChat());
                }
            }

            f.RespondJSON(c, log);
        }

        [RCEndpoint(true, "/settings", "?module={id}", "?module=CelesteNet.Server", "Server Settings", "Get the settings of any server module as YAML.")]
        public static void Settings(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);
            string? moduleID = args["module"];
            if (moduleID.IsNullOrEmpty()) {
                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                f.RespondJSON(c, new {
                    Error = "No ID."
                });
                return;
            }

            if (!f.IsAuthorizedExec(c)) {
                if (moduleID == f.Wrapper.ID || f.Settings.ExecOnlySettings.Contains(moduleID)) {
                    c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                    f.Respond(c, "Unauthorized!");
                    return;
                }
            }

            CelesteNetServerModule? module = null;
            CelesteNetServerModuleSettings? settings;

            if (moduleID == "CelesteNet.Server") {
                settings = f.Server.Settings;
            } else {
                lock (f.Server.Modules) {
                    module = f.Server.Modules.FirstOrDefault(m => m.Wrapper.ID == moduleID);
                    settings = module?.GetSettings();
                }

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
                    using (StreamReader sr = new(c.Request.InputStream, Encoding.UTF8, false, 1024, true))
                        settings.Load(sr);
                    if (module != null) {
                        // necessary to trigegr subclass overrides of this like in SqliteModule
                        module.SaveSettings();
                    } else {
                        settings.Save();
                    }
                    f.RespondJSON(c, new {
                        Info = "Success."
                    });
                    return;
                } catch (Exception e) {
                    c.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                    f.RespondJSON(c, new {
                        Error = e.ToString()
                    });
                    return;
                }
            }

            StringBuilder sb = new();
            using (StringWriter sw = new(sb))
                settings.Save(sw);
            f.Respond(c, sb.ToString());
        }

        [RCEndpoint(true, "/notes", "", "", "Admin Notes", "Get or set some administrative notes.")]
        public static void Notes(Frontend f, HttpRequestEventArgs c) {
            string path = Path.ChangeExtension(f.Settings.FilePath, ".notes.txt");
            string text;

            if (c.Request.HttpMethod == "POST") {
                try {
                    using (StreamReader sr = new(c.Request.InputStream, Encoding.UTF8, false, 1024, true))
                        text = sr.ReadToEnd();
                    File.WriteAllText(path, text);
                    f.RespondJSON(c, new {
                        Info = "Success."
                    });
                    return;
                } catch (Exception e) {
                    c.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                    f.RespondJSON(c, new {
                        Error = e.ToString()
                    });
                    return;
                }
            }

            if (!File.Exists(path)) {
                f.Respond(c, "");
                return;
            }

            try {
                text = File.ReadAllText(path);
                f.Respond(c, text);
            } catch (Exception e) {
                c.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                f.RespondJSON(c, new {
                    Error = e.ToString()
                });
                return;
            }
        }

#if NETCORE
        [RCEndpoint(true, "/exec", "", "", "Execute C#", "Run some C# code. Highly dangerous!")]
        public static void Exec(Frontend f, HttpRequestEventArgs c) {
            if (!f.IsAuthorizedExec(c)) {
                c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                f.Respond(c, "Unauthorized!");
                return;
            }

            ExecALC? alc = null;

            try {
                string name = $"CelesteNet.Server.FrontendModule.REPL.{Guid.NewGuid().ToString().Replace("-", "")}";

                string code;
                using (StreamReader sr = new(c.Request.InputStream, Encoding.UTF8, false, 1024, true))
                    code = sr.ReadToEnd();

                List<MetadataReference> refs = new();
                foreach (Assembly asmLoaded in AppDomain.CurrentDomain.GetAssemblies()) {
                    try {
                        string asmPath = asmLoaded.Location;
                        if (asmPath.IsNullOrEmpty() || !File.Exists(asmPath))
                            continue;
                        refs.Add(MetadataReference.CreateFromFile(asmPath));
                    } catch {
                    }
                }

                CSharpCompilation comp = CSharpCompilation.Create(
                    $"{name}.dll",
                    new SyntaxTree[] {
                        SyntaxFactory.ParseSyntaxTree(code, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
                    },
                    refs,
                    new(
                        OutputKind.ConsoleApplication,
                        optimizationLevel: OptimizationLevel.Release,
                        assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default
                    )
                );

                using MemoryStream ms = new();
                EmitResult result = comp.Emit(ms);
                if (!result.Success) {
                    throw new Exception("Failed building:\n" +
                        string.Join('\n', result.Diagnostics
                            .Where(d => d.IsWarningAsError || d.Severity == DiagnosticSeverity.Error)
                            .Select(d => $"{d.Id}: {d.GetMessage()}"))
                    );
                }

                ms.Seek(0, SeekOrigin.Begin);

                alc = new(name);
                alc.Resolving += (ctx, name) => {
                    foreach (CelesteNetServerModuleWrapper wrapper in f.Server.ModuleWrappers)
                        if (wrapper.ID == name.Name)
                            return wrapper.Assembly;
                    return null;
                };

                Assembly asm = alc.LoadFromStream(ms);
                if (asm.EntryPoint == null)
                    throw new Exception("No entry point found");

                try {
                    object? rv = asm.EntryPoint.Invoke(null, new object[asm.EntryPoint.GetParameters().Length]);
                    f.Respond(c, rv?.ToString() ?? "null");
                } catch (TargetInvocationException tie) when (tie.InnerException is Exception e && e.TargetSite == asm.EntryPoint) {
                    f.Respond(c, e.Message);
                }

            } catch (Exception e) {
                Logger.Log(LogLevel.DEV, "frontend-exec", e.ToString());
                c.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                f.Respond(c, $"Error:\n{e}");

            } finally {
                alc?.Unload();
            }
        }

        private class ExecALC : AssemblyLoadContext {

            public ExecALC(string name)
                : base(name, isCollectible: true) {
            }

            protected override Assembly? Load(AssemblyName name) {
                return null;
            }

        }
#endif

    }
}
