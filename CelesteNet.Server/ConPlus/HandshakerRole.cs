using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
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

        public HandshakerRole(NetPlusThreadPool pool) : base(pool) {
            scheduler = new Scheduler();
            Factory = new TaskFactory(scheduler);
        }

        public override void Dispose() {
            scheduler.Dispose();
            base.Dispose();
        }

        public override RoleWorker CreateWorker(NetPlusThread thread) => new Worker(this, thread);
        
        public async Task DoHandshake(Socket sock) {
            EndPoint remoteEP = sock.RemoteEndPoint!;
            using (CancellationTokenSource tokenSrc = new CancellationTokenSource()) {
                tokenSrc.CancelAfter(HANDSHAKE_TIMEOUT);
                tokenSrc.Token.Register(() => sock.Close());
                try {
                    // Do the teapot handshake
                    if (!await TeapotHandshake(sock)) {
                        Logger.Log(LogLevel.VVV, "handshake", $"Connection from {remoteEP} failed teapot handshake");
                        sock.Shutdown(SocketShutdown.Both);
                        sock.Close();
                        return;
                    }
                } catch (Exception) {
                    if (tokenSrc.IsCancellationRequested) {
                        Logger.Log(LogLevel.VVV, "handshake", $"Handshake for connection {remoteEP} timed out, maybe an old client?");
                        return;
                    }
                    throw;
                }
            }
        }

        private async Task<bool> TeapotHandshake(Socket sock) {
            using (NetworkStream netStream = new NetworkStream(sock, false))
            using (BufferedStream bufStream = new BufferedStream(netStream))
            using (StreamReader reader = new StreamReader(bufStream))
            using (StreamWriter writer = new StreamWriter(bufStream)) {
                async Task<bool> Send500() {
                    await writer.WriteAsync(@"
HTTP/1.1 500 Internal Server Error
Connection: close

The server encountered an internal error while handling the request
                    ".Trim().Replace("\n", "\r\n"));
                    return false;
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
                    Logger.Log(LogLevel.VVV, "handshake", $"Teapot version mismatch for connection {sock.RemoteEndPoint}: {teapotVer} [client] != {TEAPOT_VERSION} [server]");
                    await writer.WriteAsync($@"
HTTP/1.1 409 Version Conflict
Connection: close

Teapot version mismatch: {teapotVer} [client] != {TEAPOT_VERSION} [server]
                    ".Trim().Replace("\n", "\r\n"));
                    return false;
                }

                return true;
            }
        }

        public override int MinThreads => 1;
        public override int MaxThreads => int.MaxValue;

        public TaskFactory Factory { get; }
        
    }
}