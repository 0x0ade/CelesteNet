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

        public const int HANDSHAKE_TIMEOUT = 5000;
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

        public HandshakerRole(NetPlusThreadPool pool, CelesteNetServer server) : base(pool) {
            Server = server;
            scheduler = new Scheduler();
            Factory = new TaskFactory(scheduler);
        }

        public override void Dispose() {
            scheduler.Dispose();
            base.Dispose();
        }

        public override RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);
        
        public async Task DoSocketHandshake(Socket sock) {
            EndPoint remoteEP = sock.RemoteEndPoint!;
            using (CancellationTokenSource tokenSrc = new CancellationTokenSource()) {
                // .NET is completly stupid, you can't cancel async socket operations
                // We literally have to kill the socket for the handshake to be able to timeout
                tokenSrc.CancelAfter(HANDSHAKE_TIMEOUT);
                tokenSrc.Token.Register(() => sock.Close());
                try {

                    // Do the teapot handshake
                    (bool teapotSuccess, string name, string uid) = await TeapotHandshake(sock);
                    if (!teapotSuccess) {
                        Logger.Log(LogLevel.VVV, "handshake", $"Connection from {remoteEP} failed teapot handshake");
                        sock.Shutdown(SocketShutdown.Both);
                        sock.Close();
                        return;
                    }
                    Logger.Log(LogLevel.VVV, "handshake", $"Connection {remoteEP} UID {uid} name {name}");

                } catch (Exception) {
                    if (tokenSrc.IsCancellationRequested) {
                        Logger.Log(LogLevel.VVV, "handshake", $"Handshake for connection {remoteEP} timed out, maybe an old client?");
                        return;
                    }
                    throw;
                }
            }
        }

        // Let's mess with web crawlers even more ;)
        // Also: I'm a Teapot
        private async Task<(bool success, string name, string uid)> TeapotHandshake(Socket sock) {
            using (NetworkStream netStream = new NetworkStream(sock, false))
            using (BufferedStream bufStream = new BufferedStream(netStream))
            using (StreamReader reader = new StreamReader(bufStream))
            using (StreamWriter writer = new StreamWriter(bufStream)) {
                async Task<(bool, string, string)> Send500() {
                    await writer.WriteAsync(@"
HTTP/1.1 500 Internal Server Error
Connection: close

The server encountered an internal error while handling the request
                    ".Trim().Replace("\n", "\r\n"));
                    return (false, null!, null!);
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
                    return (false, null!, null!);
                }

                // Get the player name and token
                headers.TryGetValue("CelesteNet-PlayerName", out string? playerName);
                headers.TryGetValue("CelesteNet-PlayerToken", out string? playerToken);

                // Get the UID from the player name/token
                string? uid = null;
                if (playerToken != null && (uid = Server.UserData.GetUID(playerToken)) != null)
                    playerName ??= Server.UserData.Load<BasicUserInfo>(uid!).Name;
                else if (playerName != null && !Server.Settings.AuthOnly && sock.RemoteEndPoint is IPEndPoint ipEP)
                    uid = $"anon-{BitConverter.ToString(ipEP.Address.MapToIPv6().GetAddressBytes())}";

                if (uid == null) {
                    Logger.Log(LogLevel.VVV, "teapot", $"Couldn't get a valid UID for connection {sock.RemoteEndPoint}");
                    await writer.WriteAsync($@"
HTTP/1.1 403 Access Denied
Connection: close

{Server.Settings.MessageNoUID}
                    ".Trim().Replace("\n", "\r\n"));
                    return (false, null!, null!);
                }

                // Check if the player's banned
                if (Server.UserData.TryLoad<BanInfo>(uid!, out BanInfo banInfo) && (banInfo.From == null || banInfo.From <= DateTime.Now) && (banInfo.To == null || DateTime.Now <= banInfo.To)) {
                    Logger.Log(LogLevel.VVV, "teapot", $"Rejected connection from banned player {sock.RemoteEndPoint}");
                    await writer.WriteAsync($@"
HTTP/1.1 403 Access Denied
Connection: close

{string.Format(Server.Settings.MessageBan, banInfo.Reason)}
                    ".Trim().Replace("\n", "\r\n"));
                    return (false, null!, null!);
                }

                // Sanitize the players name
                playerName = playerName.Sanitize(CelesteNetPlayerSession.IllegalNameChars);
                if (playerName.Length > Server.Settings.MaxNameLength)
                    playerName = playerName.Substring(0, Server.Settings.MaxNameLength);
                if (playerName.IsNullOrEmpty())
                    playerName = "Guest";

                // Answer with the almighty teapot
                await writer.WriteAsync($@"
HTTP/1.1 418 I'm a teapot
Connection: close
CelesteNet-TeapotVersion: {TEAPOT_VERSION}

Who wants some tea?
                ".Trim().Replace("\n", "\r\n"));

                return (true, playerName!, uid!);
            }
        }

        public override int MinThreads => 1;
        public override int MaxThreads => int.MaxValue;

        public CelesteNetServer Server { get; }
        public TaskFactory Factory { get; }
        
    }
}
