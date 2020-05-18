using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientSettings : EverestModuleSettings {

        [YamlIgnore]
        public bool Connection {
            get {
                return false; // GhostNetModule.Instance.Client?.Connection != null;
            }
            set {
                if (value) {
                    // GhostNetModule.ResetGhostModuleSettings();

                    // GhostNetModule.Instance.Start();
                } else {
                    // GhostNetModule.Instance.Stop();
                }
                if (ServerEntry != null)
                    ServerEntry.Disabled = value;
            }
        }
        [YamlIgnore]
        [SettingIgnore]
        public TextMenu.OnOff EnabledEntry { get; protected set; }

        [SettingIgnore]
        [YamlMember(Alias = "Server")]
        public string _Server { get; set; } = "celeste.0x0ade.ga";
        [YamlIgnore]
        public string Server {
            get {
                return _Server;
            }
            set {
                _Server = value;

                // if (Connection)
                    // GhostNetModule.Instance.Start();
            }
        }
        [YamlIgnore]
        [SettingIgnore]
        public TextMenu.Button ServerEntry { get; protected set; }

    }
}
