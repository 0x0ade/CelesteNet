using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientModule : EverestModule {

        public static CelesteNetClientModule Instance;

        public override Type SettingsType => typeof(CelesteNetClientSettings);
        public CelesteNetClientSettings Settings => (CelesteNetClientSettings) _Settings;

        public CelesteNetClientModule() {
            Instance = this;
        }

        public override void Load() {
            Logger.LogCelesteNetTag = true;
        }

        public override void Unload() {
        }

    }
}
