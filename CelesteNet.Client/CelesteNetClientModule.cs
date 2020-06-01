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

        public CelesteNetClientComponent ClientComponent;
        public CelesteNetClient Client => ClientComponent?.Client;
        private object ClientLock = new object();

        private Thread _StartThread;
        public bool IsAlive => ClientComponent != null;

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
                if (Client?.IsAlive ?? false)
                    Stop();

                if (ClientComponent != null)
                    return;

                ClientComponent = new CelesteNetClientComponent(Celeste.Instance);
                Celeste.Instance.Components.Add(ClientComponent);

                ClientComponent.SetStatus("Initializing...");

                RunThread.Start(() => {
                    ClientComponent.Init(Settings);
                    ClientComponent.SetStatus("Connecting...");
                    ClientComponent.Start();
                    ClientComponent.SetStatus(null);
                    _StartThread = null;
                }, "CelesteNet Client Start", true);

                RunThread.Current.TryGetTarget(out _StartThread);
                if (!(_StartThread?.IsAlive ?? false))
                    _StartThread = null;
            }
        }

        public void Stop() {
            lock (ClientLock) {
                if (ClientComponent == null)
                    return;

                if (_StartThread?.IsAlive ?? false)
                    _StartThread.Abort();

                Celeste.Instance.Components.Remove(ClientComponent);
                ClientComponent.Dispose();
                ClientComponent = null;
            }
        }

    }
}
