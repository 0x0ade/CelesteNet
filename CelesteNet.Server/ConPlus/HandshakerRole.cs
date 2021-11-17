using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    /*
    This role creates a task scheduler, whose tasks are run on all threads
    assigned to the role. The connection acceptor role creates new tasks for
    that scheduler, which perform the handshake, before passing control of the
    connection to the regular send/recv roles.
    - Popax21
    */
    public partial class HandshakerRole : NetPlusThreadRole {

        public const int TEAPOT_TIMEOUT = 5000;
        public const int TEAPOT_VERSION = 1;

        private class Scheduler : TaskScheduler, IDisposable {

            private BlockingCollection<Task> taskQueue;
            private ThreadLocal<bool> executingTasks;

            public Scheduler() {
                taskQueue = new BlockingCollection<Task>();
                executingTasks = new ThreadLocal<bool>();
            }

            public void Dispose() {
                executingTasks.Dispose();
                taskQueue.Dispose();
            }

            public void ExecuteTasks(CancellationToken token) {
                executingTasks.Value = true;
                foreach (Task t in taskQueue.GetConsumingEnumerable(token))
                    TryExecuteTask(t);
                executingTasks.Value = false;
            }

            protected override IEnumerable<Task> GetScheduledTasks() => taskQueue;
            protected override void QueueTask(Task task) => taskQueue.Add(task);

            protected override bool TryExecuteTaskInline(Task task, bool prevQueued) {
                if (prevQueued || !executingTasks.Value)
                    return false;
                return TryExecuteTask(task);
            }

        }

        private class Worker : RoleWorker {

            public Worker(HandshakerRole role, NetPlusThread thread) : base(role, thread) {}

            protected internal override void StartWorker(CancellationToken token) => Role.scheduler.ExecuteTasks(token);

            public new HandshakerRole Role => (HandshakerRole) base.Role;

        }

        private Scheduler scheduler;
        private List<(string name, IConnectionFeature feature)> conFeatures;

        public HandshakerRole(NetPlusThreadPool pool, CelesteNetServer server) : base(pool) {
            Server = server;
            scheduler = new Scheduler();
            Factory = new TaskFactory(scheduler);

            // Find connection features
            conFeatures = new List<(string, IConnectionFeature)>();
            foreach (Type type in CelesteNetUtils.GetTypes()) {
                if (!typeof(IConnectionFeature).IsAssignableFrom(type) || type.IsAbstract)
                    continue;

                IConnectionFeature? feature = (IConnectionFeature?) Activator.CreateInstance(type);
                if (feature == null)
                    throw new Exception($"Cannot create instance of connection feature {type.FullName}");
                Logger.Log(LogLevel.VVV, "handshake", $"Found connection feature: {type.FullName}");
                conFeatures.Add((type.FullName!, feature!));
            }
        }

        public override void Dispose() {
            scheduler.Dispose();
            base.Dispose();
        }

        public override RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);
        
        public async Task DoTCPUDPHandshake(Socket sock) {
            EndPoint remoteEP = sock.RemoteEndPoint!;
            CelesteNetTCPUDPConnection? con = null;
            try {
                // Do the teapot handshake
                bool teapotSuccess;
                string conUID = null!;
                IConnectionFeature[] conFeatures = null!;
                string playerUID = null!, playerName = null!;
                using (CancellationTokenSource tokenSrc = new CancellationTokenSource()) {
                    // .NET is completly stupid, you can't cancel async socket operations
                    // We literally have to kill the socket for the handshake to be able to timeout
                    tokenSrc.CancelAfter(TEAPOT_TIMEOUT);
                    tokenSrc.Token.Register(() => sock.Close());
                    try {
                        var teapotRes = await TeapotHandshake(sock);
                        teapotSuccess = teapotRes != null;
                        if (teapotRes != null)
                            (conUID, conFeatures, playerUID, playerName) = teapotRes.Value;
                    } catch (Exception) {
                        if (tokenSrc.IsCancellationRequested) {
                            Logger.Log(LogLevel.VVV, "tcpudphs", $"Handshake for connection {remoteEP} timed out, maybe an old client?");
                            sock.Dispose();
                            return;
                        }
                        throw;
                    }
                }

                if (!teapotSuccess) {
                    Logger.Log(LogLevel.VVV, "tcpudphs", $"Connection from {remoteEP} failed teapot handshake");
                    sock.Shutdown(SocketShutdown.Both);
                    sock.Close();
                    return;
                }
                Logger.Log(LogLevel.VVV, "tcpudphs", $"Connection {remoteEP} teapot handshake success: connection UID {conUID} connection features '{conFeatures.Aggregate((string) null!, (a, f) => ((a == null) ? $"{f}" : $"{a}, {f}"))}' player UID {playerUID} player name {playerName}");

                // Create the connection, do the generic connection handshake and create a session
                Server.HandleConnect(con = new CelesteNetTCPUDPConnection(Server.Data, sock, conUID));
                await DoConnectionHandshake(con, conFeatures);
                Server.CreateSession(con, playerUID, playerName);
            } catch(Exception) {
                con?.Dispose();
                sock.Dispose();
                throw;
            }
        }

        // Let's mess with web crawlers even more ;)
        // Also: I'm a Teapot
        private async Task<(string conUID, IConnectionFeature[] conFeatures, string playerUID, string playerName)?> TeapotHandshake(Socket sock) {
            using (NetworkStream netStream = new NetworkStream(sock, false))
            using (BufferedStream bufStream = new BufferedStream(netStream))
            using (StreamReader reader = new StreamReader(bufStream))
            using (StreamWriter writer = new StreamWriter(bufStream)) {
                string conUID = sock.RemoteEndPoint switch {
                    IPEndPoint ipEP => $"con-ip-{BitConverter.ToString(ipEP.Address.MapToIPv6().GetAddressBytes())}",
                    _ => $"con-unknown"
                };
                    
                async Task<(string, IConnectionFeature[], string, string)?> Send500() {
                    await writer.WriteAsync(@"
HTTP/1.1 500 Internal Server Error
Connection: close

The server encountered an internal error while handling the request
                    ".Trim().Replace("\n", "\r\n"));
                    return null;
                }

                // Parse the "HTTP" request line
                string reqLine = (await reader.ReadLineAsync())!;
                string[] reqLineSegs = reqLine.Split(" ").Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (
                    reqLineSegs.Length != 3 ||
                    reqLineSegs[0] != "CONNECT" ||
                    reqLineSegs[1] != "/teapot"
                )
                    return await Send500();

                // Parse the headers
                Dictionary<string, string> headers = new Dictionary<string, string>();
                for (string line = (await reader.ReadLineAsync())!; !string.IsNullOrEmpty(line); line = (await reader.ReadLineAsync())!) {
                    string[] lineSegs = line.Split(":").Select(s => s.Trim()).ToArray()!;
                    if (lineSegs.Length < 2)
                        return await Send500();
                    headers[lineSegs[0]] = lineSegs[1];
                }

                // Check teapot version
                if (!headers.TryGetValue("CelesteNet-TeapotVersion", out string? teapotVerHeader) || !int.TryParse(teapotVerHeader!, out int teapotVer))
                    return await Send500();

                if (teapotVer != TEAPOT_VERSION) {
                    Logger.Log(LogLevel.VVV, "teapot", $"Teapot version mismatch for connection {sock.RemoteEndPoint}: {teapotVer} [client] != {TEAPOT_VERSION} [server]");
                    await writer.WriteAsync($@"
HTTP/1.1 409 Version Mismatch
Connection: close

{string.Format(Server.Settings.MessageTeapotVersionMismatch, teapotVer, TEAPOT_VERSION)}
                    ".Trim().Replace("\n", "\r\n"));
                    return null;
                }

                // Get the list of supported connection features
                HashSet<string> conFeatures;
                if (headers.TryGetValue("CelesteNet-ConnectionFeatures", out string? conFeaturesRaw))
                    conFeatures = (conFeaturesRaw!).Split(",").Select(f => f.Trim().ToLower()).ToHashSet();
                else
                    conFeatures = new HashSet<string>();

                // Match connection features
                List<(string name, IConnectionFeature feature)> matchedFeats = new List<(string, IConnectionFeature)>();
                foreach ((string name, IConnectionFeature feat) in this.conFeatures) {
                    if (conFeatures.Contains(name.ToLower()))
                        matchedFeats.Add((name, feat));
                }

                // Get the player name-key
                if (!headers.TryGetValue("CelesteNet-PlayerNameKey", out string? playerNameKey))
                    return await Send500();

                // Authenticate name-key
                ((string uid, string name)? playerData, string? errorReason) = AuthenticatePlayerNameKey(conUID, playerNameKey!);

                if (errorReason != null) {
                    Logger.Log(LogLevel.VVV, "teapot", $"Error authenticating name-key '{playerNameKey}' for connection {sock.RemoteEndPoint}: {errorReason}");
                    await writer.WriteAsync($@"
HTTP/1.1 403 Access Denied
Connection: close

{errorReason}
                    ".Trim().Replace("\n", "\r\n"));
                    return null;
                }

                // Answer with the almighty teapot
                await writer.WriteAsync($@"
HTTP/1.1 418 I'm a teapot
Connection: close
CelesteNet-TeapotVersion: {TEAPOT_VERSION}
CelesteNet-ConnectionFeatures: {matchedFeats.Aggregate((string) null!, (a, f) => ((a == null) ? f.name : $"{a}, {f.name}"))}

Who wants some tea?
                ".Trim().Replace("\n", "\r\n"));

                return (conUID, matchedFeats.Select(f => f.feature).ToArray(), (playerData!).Value.uid, (playerData!).Value.name);
            }
        }

        public async Task DoConnectionHandshake(CelesteNetConnection con, IConnectionFeature[] features) {
            foreach (IConnectionFeature feature in features)
                feature.Register(con);
            foreach (IConnectionFeature feature in features)
                await feature.DoHandShake(con);
        }

        public ((string playerUID, string playerName)?, string? errorReason) AuthenticatePlayerNameKey(string conUID, string nameKey) {
            // Get the player UID and name from the player name-key
            string playerUID = null!, playerName = null!;
            if ((nameKey!).StartsWith('#')) {
                playerUID = Server.UserData.GetUID((nameKey!).Substring(1));
                if (playerUID != null && Server.UserData.TryLoad<BasicUserInfo>(playerUID, out BasicUserInfo info))
                    playerName = info.Name!;
                else
                    return (null, string.Format(Server.Settings.MessageInvalidKey, conUID, nameKey));
            } else if (!Server.Settings.AuthOnly) {
                playerName = nameKey!;
                playerUID = conUID;
            } else
                return (null, string.Format(Server.Settings.MessageAuthOnly, conUID, nameKey));

            // Check if the player's banned
            BanInfo? ban = null;
            if (Server.UserData.TryLoad<BanInfo>(playerUID!, out BanInfo banInfo) && (banInfo.From == null || banInfo.From <= DateTime.Now) && (banInfo.To == null || DateTime.Now <= banInfo.To))
                ban = banInfo;
            if (Server.UserData.TryLoad<BanInfo>(conUID, out BanInfo conBanInfo) && (conBanInfo.From == null || conBanInfo.From <= DateTime.Now) && (conBanInfo.To == null || DateTime.Now <= conBanInfo.To))
                ban = conBanInfo;
            if (ban != null)
                return (null, string.Format(Server.Settings.MessageBan, conUID, playerUID, playerName, ban.Reason));

            // Sanitize the player's name
            playerName = playerName.Sanitize(CelesteNetPlayerSession.IllegalNameChars);
            if (playerName.Length > Server.Settings.MaxNameLength)
                playerName = playerName.Substring(0, Server.Settings.MaxNameLength);
            if (playerName.IsNullOrEmpty())
                playerName = "Guest";
        
            return ((playerUID, playerName), null);
        }

        public override int MinThreads => 1;
        public override int MaxThreads => int.MaxValue;

        public CelesteNetServer Server { get; }
        public TaskFactory Factory { get; }
        
    }
}
