using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientModule : EverestModule {

        public static CelesteNetClientModule Instance;

        public override Type SettingsType => typeof(CelesteNetClientSettings);
        public CelesteNetClientSettings Settings => (CelesteNetClientSettings) _Settings;

        public CelesteNetClientComponent ContextLast;
        public CelesteNetClientComponent Context;
        public CelesteNetClient Client => Context?.Client;
        private object ClientLock = new object();

        private Thread _StartThread;
        public bool IsAlive => Context != null;

        public CelesteNetClientModule() {
            Instance = this;
        }

        public override void Load() {
            Logger.LogCelesteNetTag = true;
        }

        public override void Unload() {
            Stop();
        }

        public void Start() {
            lock (ClientLock) {
                CelesteNetClientComponent last = Context ?? ContextLast;
                if (Client?.IsAlive ?? false)
                    Stop();

                if (Context != null)
                    return;

                last?.Status?.Set(null);
                Context = new CelesteNetClientComponent(Celeste.Instance);
                ContextLast = Context;

                Context.Status.Set("Initializing...");

                _StartThread = new Thread(() => {
                    CelesteNetClientComponent context = Context;
                    try {
                        context.Init(Settings);
                        context.Status.Set("Connecting...");
                        context.Start();
                        context.Status.Set("Connected", 1f);

                    } catch (ThreadInterruptedException) {
                        Logger.Log(LogLevel.CRI, "clientmod", "Startup interrupted.");
                        _StartThread = null;
                        Stop();
                        context.Status.Set("Interrupted", 3f, false);

                    } catch (ThreadAbortException) {
                        _StartThread = null;
                        Stop();

                    } catch (Exception e) {
                        Logger.Log(LogLevel.CRI, "clientmod", $"Failed connecting:\n{e}");
                        _StartThread = null;
                        Stop();
                        context.Status.Set("Connection failed", 3f, false);

                    } finally {
                        _StartThread = null;
                    }
                }) {
                    Name = "CelesteNet Client Start",
                    IsBackground = true
                };
                _StartThread.Start();
            }
        }

        public void Stop() {
            lock (ClientLock) {
                if (_StartThread?.IsAlive ?? false)
                    _StartThread.Interrupt();

                if (Context == null)
                    return;

                ContextLast = Context;
                Context.Dispose();
                Context = null;
            }
        }

    }
}
