using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.UI;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Celeste.Mod.CelesteNet.Client {
    [SettingName("modoptions_celestenetclient_title")]
    public class CelesteNetClientSettings : EverestModuleSettings {

        [SettingIgnore]
        [YamlIgnore]
        public bool WantsToBeConnected { get; protected set; }

        [YamlIgnore]
        public bool Connected {
            get => CelesteNetClientModule.Instance.IsAlive;
            set {
                WantsToBeConnected = value;

                if (value)
                    CelesteNetClientModule.Instance.Start();
                else
                    CelesteNetClientModule.Instance.Stop();

                if (!value && EnabledEntry != null && Engine.Scene != null)
                    Engine.Scene.OnEndOfFrame += () => EnabledEntry?.LeftPressed();
                if (ServerEntry != null)
                    ServerEntry.Disabled = value || !(Engine.Scene is Overworld);
                if (NameEntry != null)
                    NameEntry.Disabled = value || !(Engine.Scene is Overworld);
            }
        }
        [YamlIgnore]
        [SettingIgnore]
        public TextMenu.OnOff EnabledEntry { get; protected set; }

        public bool AutoReconnect { get; set; } = true;

        public string Server { get; set; } = "celeste.0x0ade.ga";
        [YamlIgnore]
        [SettingIgnore]
        public TextMenu.Button ServerEntry { get; protected set; }

        public string Name { get; set; } = "Guest";
        [YamlIgnore]
        [SettingIgnore]
        public TextMenu.Button NameEntry { get; protected set; }


#if !DEBUG
        [SettingIgnore]
#endif
        [SettingSubText("modoptions_celestenetclient_devonlyhint")]
        public ConnectionType ConnectionType { get; set; } = ConnectionType.Auto;


#if !DEBUG
        [SettingIgnore]
#endif
        [SettingSubText("modoptions_celestenetclient_devonlyhint")]
        public LogLevel DevLogLevel {
            get => Logger.Level;
            set => Logger.Level = value;
        }

        public bool Collision { get; set; } = true;
        public bool Sounds { get; set; } = true;

        public CelesteNetPlayerListComponent.ListMode PlayerListMode { get; set; }

        [SettingIgnore]
        [SettingRange(4, 16)]
        public int ChatLogLength { get; set; } = 8;

        public const int UISizeMin = 1;
        public const int UISizeReachableMax = 4;
        public const int UISizeMax = 5;
        [SettingRange(UISizeMin, UISizeReachableMax)]
        public int UISize { get; set; } = 2;
        [SettingIgnore]
        [YamlIgnore]
        public float UIScale => 0.5f + 0.5f * ((UISize - 1f) / (UISizeMax - 1f));

        #region Key Bindings

        [DefaultButtonBinding(Buttons.Back, Keys.Tab)]
        public ButtonBinding ButtonPlayerList { get; set; }

        [DefaultButtonBinding(0, Keys.T)]
        public ButtonBinding ButtonChat { get; set; }

        #endregion


        [SettingIgnore]
        public string[] Emotes { get; set; }



        #region Helpers

        [SettingIgnore]
        [YamlIgnore]
        public string Host {
            get {
                string server = Server?.ToLowerInvariant();
                int indexOfPort;
                if (!string.IsNullOrEmpty(server) &&
                    (indexOfPort = server.LastIndexOf(':')) != -1 &&
                    int.TryParse(server.Substring(indexOfPort + 1), out _))
                    return server.Substring(0, indexOfPort);

                return server;
            }
        }
        [SettingIgnore]
        [YamlIgnore]
        public int Port {
            get {
                string server = Server;
                int indexOfPort;
                if (!string.IsNullOrEmpty(server) &&
                    (indexOfPort = server.LastIndexOf(':')) != -1 &&
                    int.TryParse(server.Substring(indexOfPort + 1), out int port))
                    return port;

                return 3802;
            }
        }

        #endregion


        #region Custom Entry Creators

        public void CreateConnectedEntry(TextMenu menu, bool inGame) {
            menu.Add(
                (EnabledEntry = new TextMenu.OnOff("modoptions_celestenetclient_connected".DialogClean(), Connected))
                .Change(v => Connected = v)
            );
        }

        public void CreateServerEntry(TextMenu menu, bool inGame) {
            menu.Add(
                (ServerEntry = new TextMenu.Button(("modoptions_celestenetclient_server".DialogClean()).Replace("((server))", Server)))
                .Pressed(() => {
                    Audio.Play("event:/ui/main/savefile_rename_start");
                    menu.SceneAs<Overworld>().Goto<OuiModOptionString>().Init<OuiModOptions>(
                        Server,
                        v => Server = v,
                        maxValueLength: 30
                    );
                })
            );
            ServerEntry.Disabled = inGame || Connected;
        }

        public void CreateNameEntry(TextMenu menu, bool inGame) {
            string name = Name;
            if (name.StartsWith("#"))
                name = "########";

            menu.Add(
                (NameEntry = new TextMenu.Button(("modoptions_celestenetclient_name".DialogClean()).Replace("((name))", name)))
                .Pressed(() => {
                    Audio.Play("event:/ui/main/savefile_rename_start");
                    menu.SceneAs<Overworld>().Goto<OuiModOptionString>().Init<OuiModOptions>(
                        Name,
                        v => Name = v,
                        maxValueLength: 20
                    );
                })
            );
            NameEntry.Disabled = inGame || Connected;
        }

        public void CreateEmotesEntry(TextMenu menu, bool inGame) {
            TextMenu.Item item;
            
            menu.Add(item = new TextMenu.Button("modoptions_celestenetclient_reload".DialogClean()).Pressed(() => {
                CelesteNetClientSettings settingsOld = CelesteNetClientModule.Settings;
                CelesteNetClientModule.Instance.LoadSettings();
                CelesteNetClientSettings settingsNew = CelesteNetClientModule.Settings;
                CelesteNetClientModule.Instance._Settings = settingsOld;

                settingsOld.Emotes = settingsNew.Emotes;
            }));

            item.AddDescription(menu, "modoptions_celestenetclient_reloadhint".DialogClean());
        }

        #endregion

    }
}
