using Celeste.Mod.UI;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Celeste.Mod.CelesteNet.Client {
    [SettingName("modoptions_celestenet_title")]
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
                    ServerEntry.Disabled = value || !(Engine.Scene is Overworld);
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


#if !DEBUG
        [SettingIgnore]
#endif
        public LogLevel LogLevel {
            get => Logger.Level;
            set => Logger.Level = value;
        }


#region Custom Entry Creators

        public void CreateConnectionEntry(TextMenu menu, bool inGame) {
            menu.Add(
                (EnabledEntry = new TextMenu.OnOff("modoptions_celestenet_connected".DialogClean(), Connection))
                .Change(v => Connection = v)
            );
        }

        public void CreateServerEntry(TextMenu menu, bool inGame) {
            menu.Add(
                (ServerEntry = new TextMenu.Button(("modoptions_celestenet_server".DialogClean()) + ": " + Server))
                .Pressed(() => {
                    Audio.Play("event:/ui/main/savefile_rename_start");
                    menu.SceneAs<Overworld>().Goto<OuiModOptionString>().Init<OuiModOptions>(
                        Server,
                        v => Server = v,
                        maxValueLength: 30
                    );
                })
            );
            ServerEntry.Disabled = inGame || Connection;
        }

#endregion

    }
}
