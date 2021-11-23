using Celeste.Mod.CelesteNet.DataTypes;
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
    -Popax21
    */
    public partial class HandshakerRole : NetPlusThreadRole {

        public const int TeapotTimeout = 5000;
        public const int TeapotVersion = 1;

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

            public void ExecuteTasks(Worker worker, CancellationToken token) {
                executingTasks.Value = true;
                foreach (Task t in taskQueue.GetConsumingEnumerable(token)) {
                    worker.EnterActiveZone();
                    try {
                        TryExecuteTask(t);
                    } finally {
                        worker.ExitActiveZone();
                    }
                }
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

            protected internal override void StartWorker(CancellationToken token) => Role.scheduler.ExecuteTasks(this, token);

            public new void EnterActiveZone() => base.EnterActiveZone();
            public new void ExitActiveZone() => base.ExitActiveZone();

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

        public async Task DoTCPUDPHandshake(Socket sock, CelesteNetTCPUDPConnection.Settings settings, TCPReceiverRole tcpReceiver, UDPReceiverRole udpReceiver, TCPUDPSenderRole sender) {
            EndPoint remoteEP = sock.RemoteEndPoint!;
            ConPlusTCPUDPConnection? con = null;
            try {
                // Get the connection UID
                string conUID = $"con-tpcudp-{BitConverter.ToString(((IPEndPoint) sock.RemoteEndPoint!).Address.MapToIPv6().GetAddressBytes())}";

                // Obtain a connection token
                int conToken = Server.ConTokenGenerator.GenerateToken();

                // Do the teapot handshake
                bool teapotSuccess;
                IConnectionFeature[] conFeatures = null!;
                string playerUID = null!, playerName = null!;
                using (CancellationTokenSource tokenSrc = new CancellationTokenSource()) {
                    // .NET is completly stupid, you can't cancel async socket operations
                    // We literally have to kill the socket for the handshake to be able to timeout
                    tokenSrc.CancelAfter(TeapotTimeout);
                    tokenSrc.Token.Register(() => sock.Close());
                    try {
                        var teapotRes = await TeapotHandshake(sock, conUID, conToken, settings, ((IPEndPoint) udpReceiver.EndPoint).Port, ((IPEndPoint) sender.UDPEndPoint).Port);
                        teapotSuccess = teapotRes != null;
                        if (teapotRes != null)
                            (conFeatures, playerUID, playerName) = teapotRes.Value;
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
                    sock.ShutdownSafe(SocketShutdown.Both);
                    sock.Close();
                    return;
                }
                Logger.Log(LogLevel.VVV, "tcpudphs", $"Connection {remoteEP} teapot handshake success: connection UID {conUID} connection features '{conFeatures.Aggregate((string) null!, (a, f) => ((a == null) ? $"{f}" : $"{a}, {f}"))}' player UID {playerUID} player name {playerName}");

                // Create the connection, do the generic connection handshake and create a session
                Server.HandleConnect(con = new ConPlusTCPUDPConnection(Server, conUID, conToken, settings, sock, tcpReceiver, udpReceiver, sender));
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
        private async Task<(IConnectionFeature[] conFeatures, string playerUID, string playerName)?> TeapotHandshake(Socket sock, string conUID,int conToken, CelesteNetTCPUDPConnection.Settings settings, int udpRecvPort, int udpSendPort) {
            using (NetworkStream netStream = new NetworkStream(sock, false))
            using (BufferedStream bufStream = new BufferedStream(netStream))
            using (StreamReader reader = new StreamReader(bufStream))
            using (StreamWriter writer = new StreamWriter(bufStream)) {
                async Task<(IConnectionFeature[], string, string)?> Send500() {
                    await writer.WriteAsync(@"
HTTP/1.1 500 Internal Server Error
Connection: close

The server encountered an internal error while handling the request
                    ".Trim().Replace("\n", "\r\n"));
                    return null;
                }

                // Parse the "HTTP" request line
                string? reqLine = (await reader.ReadLineAsync());
                if (reqLine == null)
                    return await Send500();

                string[] reqLineSegs = (reqLine!).Split(' ').Where(s => !string.IsNullOrEmpty(s)).ToArray();
                if (
                    reqLineSegs.Length != 3 ||
                    reqLineSegs[0] != "CONNECT" ||
                    reqLineSegs[1] != "/teapot"
                )
                    return await Send500();

                // Parse the headers
                Dictionary<string, string> headers = new Dictionary<string, string>();
                for (string? line = (await reader.ReadLineAsync()); !string.IsNullOrEmpty(line); line = await reader.ReadLineAsync()) {
                    string[] lineSegs = (line!).Split(':', 2).Select(s => s.Trim()).ToArray()!;
                    if (lineSegs.Length < 2)
                        return await Send500();
                    headers[lineSegs[0]] = lineSegs[1];
                }
                bufStream.Flush();

                // Check teapot version
                if (!headers.TryGetValue("CelesteNet-TeapotVersion", out string? teapotVerHeader) || !int.TryParse(teapotVerHeader!, out int teapotVer))
                    return await Send500();

                if (teapotVer != TeapotVersion) {
                    Logger.Log(LogLevel.VVV, "teapot", $"Teapot version mismatch for connection {sock.RemoteEndPoint}: {teapotVer} [client] != {TeapotVersion} [server]");
                    await writer.WriteAsync($@"
HTTP/1.1 409 Version Mismatch
Connection: close

{string.Format(Server.Settings.MessageTeapotVersionMismatch, teapotVer, TeapotVersion)}
                    ".Trim().Replace("\n", "\r\n"));
                    return null;
                }

                // Get the list of supported connection features
                HashSet<string> conFeatures;
                if (headers.TryGetValue("CelesteNet-ConnectionFeatures", out string? conFeaturesRaw))
                    conFeatures = (conFeaturesRaw!).Split(',').Select(f => f.Trim().ToLower()).ToHashSet();
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
CelesteNet-TeapotVersion: {TeapotVersion}
CelesteNet-ConnectionToken: {conToken}
CelesteNet-ConnectionFeatures: {matchedFeats.Aggregate((string) null!, (a, f) => ((a == null) ? f.name : $"{a}, {f.name}"))}
CelesteNet-MaxPacketSize: {settings.MaxPacketSize}
CelesteNet-MaxQueueSize: {settings.MaxQueueSize}
CelesteNet-MergeWindow: {settings.MergeWindow}
CelesteNet-MaxHeartbeatDelay: {settings.MaxHeartbeatDelay}
CelesteNet-HeartbeatInterval: {settings.HeartbeatInterval}
CelesteNet-UDPAliveScoreMax: {settings.UDPAliveScoreMax}
CelesteNet-UDPDowngradeScoreMin: {settings.UDPDowngradeScoreMin}
CelesteNet-UDPDowngradeScoreMax: {settings.UDPDowngradeScoreMax}
CelesteNet-UDPDeathScoreMin: {settings.UDPDeathScoreMin}
CelesteNet-UDPDeathScoreMax: {settings.UDPDeathScoreMax}
CelesteNet-UDPReceivePort: {udpRecvPort}
CelesteNet-UDPSendPort: {udpSendPort}

Who wants some tea?
                ".Trim().Replace("\n", "\r\n") + "\r\n" + "\r\n");

                return (matchedFeats.Select(f => f.feature).ToArray(), (playerData!).Value.uid, (playerData!).Value.name);
            }
        }

        public async Task DoConnectionHandshake(CelesteNetConnection con, IConnectionFeature[] features) {
            // Handshake connection features
            foreach (IConnectionFeature feature in features)
                feature.Register(con, false);
            foreach (IConnectionFeature feature in features)
                await feature.DoHandShake(con, false);

            // Send the current tick rate
            con.Send(new DataTickRate() {
                TickRate = Server.CurrentTickRate
            });
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
