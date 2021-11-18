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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
#endif

namespace Celeste.Mod.CelesteNet.Server.Control {
    public static partial class RCEndpoints {

        [RCEndpoint(false, "/auth", null, null, "Authenticate", "Basic POST authentication endpoint.")]
        public static void Auth(Frontend f, HttpRequestEventArgs c) {
            string? key = c.Request.Cookies[Frontend.COOKIE_SESSION]?.Value;
            string? pass;

            try {
                using StreamReader sr = new(c.Request.InputStream, Encoding.UTF8, false, 1024, true);
                using JsonTextReader jtr = new(sr);
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

            if (pass == f.Settings.PasswordExec) {
                do {
                    key = Guid.NewGuid().ToString();
                } while (!f.CurrentSessionKeys.Add(key) || !f.CurrentSessionExecKeys.Add(key));
                c.Response.SetCookie(new(Frontend.COOKIE_SESSION, key));
                f.RespondJSON(c, new {
                    Key = key
                });
                return;
            }

            if (pass == f.Settings.Password) {
                do {
                    key = Guid.NewGuid().ToString();
                } while (!f.CurrentSessionKeys.Add(key));
                c.Response.SetCookie(new(Frontend.COOKIE_SESSION, key));
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

        private static float netPlusPoolActvRate;
        private static object[]? netPlusThreadStats;
        private static object[]? netPlusRoleStats;

        private struct BandwithRate {
        
            public float GlobalRate, MinConRate, MaxConRate, AvgConRate;
        
            public void UpdateRate(CelesteNetServer server, Func<CelesteNetConnection, float?> cb) {
                GlobalRate = 0;
                MinConRate = float.MaxValue;
                MaxConRate = float.MinValue;
                AvgConRate = 0;
                int numCons = 0;
                foreach (CelesteNetConnection con in server.Connections) {
                    float? r = cb(con);
                    if (r == null) continue;
                    float conRate = r.Value;

                    GlobalRate += conRate;
                    if (conRate < MinConRate) MinConRate = conRate;
                    if (conRate > MaxConRate) MaxConRate = conRate;
                    AvgConRate += conRate;
                    numCons++;
                }
                if (numCons > 0)
                    AvgConRate /= numCons;
                else
                    MinConRate = MaxConRate = AvgConRate = 0;
            }

        }

        private static object statLock = new object();
        private static BandwithRate tcpRecvBpSRate, tcpRecvPpSRate, tcpSendBpSRate, tcpSendPpSRate;
        private static BandwithRate udpRecvBpSRate, udpRecvPpSRate, udpSendBpSRate, udpSendPpSRate;

        public static void UpdateStats(CelesteNetServer server) {
            lock (statLock) {
                // Update NetPlus stats
                using (server.ThreadPool.PoolLock.R())
                using (server.ThreadPool.RoleLock.R())
                using (server.ThreadPool.Scheduler.RoleLock.R()) {
                    netPlusPoolActvRate = server.ThreadPool.ActivityRate;
                    netPlusThreadStats = server.ThreadPool.EnumerateThreads().Select(t => new {
                        ActivityRate = t.ActivityRate,
                        Role = t.Role.ToString()
                    }).ToArray();
                    netPlusRoleStats = server.ThreadPool.Scheduler.EnumerateRoles().Select(r => new {
                        Role = r.ToString(),
                        ActivityRate = r.ActivityRate,
                        NumThreads = server.ThreadPool.EnumerateThreads().Count(t => t.Role == r)
                    }).ToArray();
                }

                // Update bandwith stats
                // TODO Downlink rates
                using (server.ConLock.R()) {
                    tcpSendBpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.TCPSendByteRate : null);
                    tcpSendPpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.TCPSendPacketRate : null);
                    udpSendBpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.UDPSendByteRate : null);
                    udpSendPpSRate.UpdateRate(server, c => (c is ConPlusTCPUDPConnection con) ? con.UDPSendPacketRate : null);
                }
            }
        }

        [RCEndpoint(false, "/status", null, null, "Server Status", "Basic server status information.")]
        public static void Status(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);
            lock (statLock)
                f.RespondJSON(c, new {
                    StartupTime = f.Server.StartupTime.ToUnixTime(),
                    GCMemory = GC.GetTotalMemory(false),
                    Modules = f.Server.Modules.Count,
                    
                    f.Server.PlayerCounter,
                    Registered = f.Server.UserData.GetRegisteredCount(),
                    Banned = f.Server.UserData.LoadAll<BanInfo>().GroupBy(ban => ban.UID).Select(g => g.First()).Count(ban => !ban.Reason.IsNullOrEmpty()),
                    
                    Connections = auth ? f.Server.Connections.Count : (int?) null,
                    Sessions = auth ? f.Server.Sessions.Count : (int?) null,
                    PlayersByCon = auth ? f.Server.PlayersByCon.Count : (int?) null,
                    PlayersByID = auth ? f.Server.PlayersByID.Count : (int?) null,
                    PlayerRefs = f.Server.Data.GetRefs<DataPlayerInfo>().Length,

                    TCPDownlinkBpS = tcpRecvBpSRate, TCPDownlinkPpS = tcpRecvPpSRate,
                    UDPDownlinkBpS = udpRecvBpSRate, UDPDownlinkPpS = udpRecvPpSRate,
                    TCPUplinkBpS = tcpSendBpSRate, TCPUplinkPpS = tcpSendPpSRate,
                    UDPUplinkBpS = udpSendBpSRate, UDPUplinkPpS = udpSendPpSRate,
                });
        }


        [RCEndpoint(false, "/netplus", null, null, "NetPlus Status", "Status information about NetPlus")]
        public static void NetPlus(Frontend f, HttpRequestEventArgs c) {
            lock (statLock)
                f.RespondJSON(c, new {
                    PoolActivityRate = netPlusPoolActvRate,
                    PoolNumThreads = f.Server.ThreadPool.NumThreads,
                    PoolThreads = netPlusThreadStats,
                    PoolRoles = netPlusRoleStats,

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
                    Key = f.Server.UserData.GetKey(uid),
                    Ban = ban.Reason.IsNullOrEmpty() ? null : new {
                        ban.Name,
                        ban.Reason,
                        From = ban.From?.ToUnixTime() ?? 0,
                        To = ban.To?.ToUnixTime() ?? 0
                    },
                    Kicks = kicks.Log.Select(e => new {
                        e.Reason,
                        From = e.From?.ToUnixTime() ?? 0
                    }).ToArray()
                };
            }).ToArray());
        }

        [RCEndpoint(false, "/players", null, null, "Player List", "Basic player list.")]
        public static void Players(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);
            f.RespondJSON(c, f.Server.PlayersByID.Values.Select(p => new {
                p.SessionID,
                UID = auth ? p.UID : null,
                p.PlayerInfo?.Name,
                p.PlayerInfo?.FullName,
                p.PlayerInfo?.DisplayName,
                
                Connection = auth ? p.Con.ID : null,
                ConnectionUID = auth ? p.Con.UID : null,

                // TODO Downlink rates
                TCPUplinkBpS = auth ? (p.Con as ConPlusTCPUDPConnection)?.TCPSendByteRate : null,
                TCPUplinkPpS = auth ? (p.Con as ConPlusTCPUDPConnection)?.TCPSendPacketRate : null,
                UDPUplinkBpS = auth ? (p.Con as ConPlusTCPUDPConnection)?.UDPSendByteRate : null,
                UDPUplinkPpS = auth ? (p.Con as ConPlusTCPUDPConnection)?.UDPSendPacketRate : null,
            }).ToArray());
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
                    if (msg != null && (msg.Target == null || auth))
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
                    using (StreamReader sr = new(c.Request.InputStream, Encoding.UTF8, false, 1024, true))
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
                f.RespondJSON(c, new {
                    Error = e.ToString()
                });
                return;
            }
        }

#if NETCORE
        [RCEndpoint(true, "/exec", "", "", "Execute C#", "Run some C# code. Highly dangerous!")]
        public static void Exec(Frontend f, HttpRequestEventArgs c) {
            if (!f.IsAuthorizedExec(c))
                f.Respond(c, "Unauthorized!");

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
                c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
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
