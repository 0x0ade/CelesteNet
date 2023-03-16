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

        #region Top Level Settings

        [SettingIgnore]
        public bool WantsToBeConnected { get; set; }

        [YamlIgnore]
        public bool Connected {
            get => CelesteNetClientModule.Instance.IsAlive;
            set {
                WantsToBeConnected = value;

                if (value && !Connected)
                    CelesteNetClientModule.Instance.Start();
                else if (!value && Connected)
                    CelesteNetClientModule.Instance.Stop();

                if (!value && EnabledEntry != null && Engine.Scene != null)
                    Engine.Scene.OnEndOfFrame += () => EnabledEntry?.LeftPressed();
                if (ServerEntry != null)
                    ServerEntry.Disabled = value || !(Engine.Scene is Overworld);
                if (NameEntry != null)
                    NameEntry.Disabled = value || !(Engine.Scene is Overworld);
            }
        }
        [SettingIgnore, YamlIgnore]
        public TextMenu.OnOff EnabledEntry { get; protected set; }

        public bool AutoReconnect { get; set; } = true;

        [SettingSubText("modoptions_celestenetclient_avatarshint")]
        public bool ReceivePlayerAvatars { get; set; } = true;
#if DEBUG
        [SettingSubHeader("modoptions_celestenetclient_subheading_general")]
#else
        [SettingIgnore]
#endif
        [SettingSubText("modoptions_celestenetclient_devonlyhint")]
        public string Server { get; set; } = "celeste.0x0a.de";
        [SettingIgnore, YamlIgnore]
        public TextMenu.Button ServerEntry { get; protected set; }

#if !DEBUG
        [SettingSubHeader("modoptions_celestenetclient_subheading_general")]
#endif
        public string Name { get; set; } = "Guest";
        [SettingIgnore, YamlIgnore]
        public TextMenu.Button NameEntry { get; protected set; }

#endregion

#region Debug

        public DebugMenu Debug { get; set; }

#if DEBUG
        [SettingSubMenu]
#endif
        public class DebugMenu {
            [SettingSubText("modoptions_celestenetclient_devonlyhint")]
            public ConnectionType ConnectionType { get; set; } = ConnectionType.Auto;

            [SettingSubText("modoptions_celestenetclient_devonlyhint")]
            public LogLevel DevLogLevel {
                get => Logger.Level;
                set => Logger.Level = value;
            }

        }

#endregion

#region In-Game

        public InGameMenu InGame { get; set; }
        [SettingSubMenu]
        public class InGameMenu {

            [SettingSubText("modoptions_celestenetclient_interactionshint")]
            public bool Interactions { get; set; } = true;

            [SettingSubText("modoptions_celestenetclient_entitieshint")]
            public SyncMode Entities { get; set; } = SyncMode.ON;
            [SettingSubHeader("modoptions_celestenetclient_subheading_players")]

            [SettingRange(0, 4)]
            public int PlayerOpacity { get; set; } = 4;

            [SettingRange(0, 4)]
            public int NameOpacity { get; set; } = 4;

            public bool ShowOwnName { get; set; } = true;

            [SettingSubHeader("modoptions_celestenetclient_subheading_sound")]
            public SyncMode Sounds { get; set; } = SyncMode.ON;

            [SettingRange(1, 10)]
            public int SoundVolume { get; set; } = 8;

        }

#endregion

#region Top-level UI

        public const int UISizeMin = 1, UISizeMax = 4;
        [SettingIgnore, YamlIgnore]
        public int _UISize { get; private set; } = 2;
        [SettingSubHeader("modoptions_celestenetclient_subheading_ui")]
        [SettingSubText("modoptions_celestenetclient_uisizehint")]
        [SettingRange(UISizeMin, UISizeMax)]
        public int UISize {
            get => _UISize;
            set {
                if (value != _UISize) {
                    // update both chat and player UI size properties to the same value
                    UISizeChat = UISizePlayerList = value;
                }
                _UISize = value;
            }
        }

        [SettingIgnore, YamlIgnore]
        public int _UISizeChat { get; private set; }
        [SettingRange(UISizeMin, UISizeMax)]
        public int UISizeChat {
            get => _UISizeChat;
            set {
                if (UISizeChatSlider != null && value != _UISizeChat) {
                    // all this is to make the OUI elements update and "react" properly (and visually)
                    if (value < _UISizeChat) {
                        UISizeChatSlider.LeftPressed();
                    } else {
                        UISizeChatSlider.RightPressed();
                    }

                    UISizeChatSlider.Index = Calc.Clamp(value, UISizeMin, UISizeMax) - 1;
                    UISizeChatSlider.OnValueChange.Invoke(value);
                }
                _UISizeChat = value;
            }
        }

        [SettingIgnore, YamlIgnore]
        public TextMenu.Slider UISizeChatSlider { get; protected set; }

        [SettingIgnore, YamlIgnore]
        public int _UISizePlayerList { get; private set; }
        [SettingRange(UISizeMin, UISizeMax)]
        public int UISizePlayerList {
            get => _UISizePlayerList;
            set {
                if (UISizePlayerListSlider != null && value != _UISizePlayerList) {
                    // all this is to make the OUI elements update and "react" properly (and visually)
                    if (value < _UISizePlayerList) {
                        UISizePlayerListSlider.LeftPressed();
                    } else {
                        UISizePlayerListSlider.RightPressed();
                    }

                    UISizePlayerListSlider.Index = Calc.Clamp(value, UISizeMin, UISizeMax) - 1;
                    UISizePlayerListSlider.OnValueChange.Invoke(value);
                }
                _UISizePlayerList = value;
            }
        }


        [SettingIgnore, YamlIgnore]
        public TextMenu.Slider UISizePlayerListSlider { get; protected set; }

        [SettingIgnore]
        public float UIScaleOverride { get; set; } = 0f;
        [SettingIgnore, YamlIgnore]
        public float UIScale => CalcUIScale(UISize);
        [SettingIgnore, YamlIgnore]
        public float UIScaleChat => CalcUIScale(UISizeChat);
        [SettingIgnore, YamlIgnore]
        public float UIScalePlayerList => CalcUIScale(UISizePlayerList);

#endregion

#region UI Chat

        public ChatUIMenu ChatUI { get; set; }
        [SettingSubMenu]
        public class ChatUIMenu {

            public CelesteNetChatComponent.ChatMode ShowNewMessages { get; set; }

            [SettingIgnore]
            [SettingRange(4, 16)]
            public int ChatLogLength { get; set; } = 8;

            [SettingRange(1, 5)]
            public int ChatScrollSpeed { get; set; } = 2;

            public CelesteNetChatComponent.ChatScrollFade ChatScrollFading { get; set; } = CelesteNetChatComponent.ChatScrollFade.Fast;

        }

#endregion

#region UI Player List

        public PlayerListUIMenu PlayerListUI { get; set; }
        [SettingSubMenu]
        public class PlayerListUIMenu {

            public CelesteNetPlayerListComponent.ListModes PlayerListMode { get; set; } = CelesteNetPlayerListComponent.ListModes.Channels;

            public CelesteNetPlayerListComponent.LocationModes ShowPlayerListLocations { get; set; } = CelesteNetPlayerListComponent.LocationModes.ON;

            [SettingIgnore]
            public bool PlayerListShortenRandomizer { get; set; } = true;

            public bool PlayerListAllowSplit { get; set; } = true;

            public bool PlayerListShowPing { get; set; } = true;

        }

#endregion

#region Performance

        // TODO: Add some more performance-focused settings like maybe skipping Blur RTs entirely
        [SettingSubText("modoptions_celestenetclient_uiblurhint")]
        public CelesteNetRenderHelperComponent.BlurQuality UIBlur { get; set; } = CelesteNetRenderHelperComponent.BlurQuality.MEDIUM;

#endregion

#region Legacy properties

        // For compatibility with other mods that access these

        // Debug
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ConnectionType is now CelesteNetClientSettings.Debug.ConnectionType")]
        public ConnectionType ConnectionType {
            get { return Debug.ConnectionType; }
            set { Debug.ConnectionType = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.DevLogLevel is now CelesteNetClientSettings.Debug.DevLogLevel")]
        public LogLevel DevLogLevel { 
            get { return Debug.DevLogLevel; }
            set { Debug.DevLogLevel = value; }
        }
        // In-Game
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.Interactions is now CelesteNetClientSettings.InGame.Interactions")]
        public bool Interactions { 
            get { return InGame?.Interactions ?? true; }
            set { if (InGame != null) InGame.Interactions = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.Sounds is now CelesteNetClientSettings.InGame.Sounds")]
        public SyncMode Sounds {
            get { return InGame?.Sounds ?? SyncMode.ON; }
            set { if (InGame != null) InGame.Sounds = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.SoundVolume is now CelesteNetClientSettings.InGame.SoundVolume")]
        public int SoundVolume {
            get { return InGame?.SoundVolume ?? 8; }
            set { if (InGame != null) InGame.SoundVolume = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.Entities is now CelesteNetClientSettings.InGame.Entities")]
        public SyncMode Entities {
            get { return InGame?.Entities ?? SyncMode.ON; }
            set { if (InGame != null) InGame.Entities = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerOpacity is now CelesteNetClientSettings.InGame.PlayerOpacity")]
        public int PlayerOpacity {
            get { return InGame?.PlayerOpacity ?? 4; }
            set { if (InGame != null) InGame.PlayerOpacity = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.NameOpacity is now CelesteNetClientSettings.InGame.NameOpacity")]
        public int NameOpacity {
            get { 
                
                return InGame?.NameOpacity ?? 4; 
            }
            set { if (InGame != null) InGame.NameOpacity = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ShowOwnName is now CelesteNetClientSettings.InGame.ShowOwnName")]
        public bool ShowOwnName {
            get { return InGame?.ShowOwnName ?? true; }
            set { if (InGame != null) InGame.ShowOwnName = value; }
        }
        // Chat UI
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ShowNewMessages is now CelesteNetClientSettings.ChatUI.ShowNewMessages")]
        public CelesteNetChatComponent.ChatMode ShowNewMessages {
            get { return ChatUI?.ShowNewMessages ?? CelesteNetChatComponent.ChatMode.All; }
            set { if (ChatUI != null) ChatUI.ShowNewMessages = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ChatLogLength is now CelesteNetClientSettings.ChatUI.ChatLogLength")]
        public int ChatLogLength {
            get { return ChatUI?.ChatLogLength ?? 8; }
            set { if (ChatUI != null) ChatUI.ChatLogLength = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ChatScrollSpeed is now CelesteNetClientSettings.ChatUI.ChatScrollSpeed")]
        public int ChatScrollSpeed {
            get { return ChatUI?.ChatScrollSpeed ?? 2; }
            set { if (ChatUI != null) ChatUI.ChatScrollSpeed = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ChatScrollFading is now CelesteNetClientSettings.ChatUI.ChatScrollFading")]
        public CelesteNetChatComponent.ChatScrollFade ChatScrollFading {
            get { return ChatUI?.ChatScrollFading ?? CelesteNetChatComponent.ChatScrollFade.Fast; }
            set { if (ChatUI != null) ChatUI.ChatScrollFading = value; }
        }
        // Player List UI
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerListMode is now CelesteNetClientSettings.PlayerListUI.PlayerListMode")]
        public CelesteNetPlayerListComponent.ListModes PlayerListMode {
            get { return PlayerListUI?.PlayerListMode ?? CelesteNetPlayerListComponent.ListModes.Channels; }
            set { if (PlayerListUI != null) PlayerListUI.PlayerListMode = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.ShowPlayerListLocations is now CelesteNetClientSettings.PlayerListUI.ShowPlayerListLocations")]
        public CelesteNetPlayerListComponent.LocationModes ShowPlayerListLocations {
            get { return PlayerListUI?.ShowPlayerListLocations ?? CelesteNetPlayerListComponent.LocationModes.ON; }
            set { if (PlayerListUI != null) PlayerListUI.ShowPlayerListLocations = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerListShortenRandomizer is now CelesteNetClientSettings.PlayerListUI.PlayerListShortenRandomizer")]
        public bool PlayerListShortenRandomizer {
            get { return PlayerListUI?.PlayerListShortenRandomizer ?? true; }
            set { if (PlayerListUI != null) PlayerListUI.PlayerListShortenRandomizer = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerListAllowSplit is now CelesteNetClientSettings.PlayerListUI.PlayerListAllowSplit")]
        public bool PlayerListAllowSplit {
            get { return PlayerListUI?.PlayerListAllowSplit ?? true; }
            set { if (PlayerListUI != null) PlayerListUI.PlayerListAllowSplit = value; }
        }
        [SettingIgnore, YamlIgnore]
        [Obsolete("CelesteNetClientSettings.PlayerListShowPing is now CelesteNetClientSettings.PlayerListUI.PlayerListShowPing")]
        public bool PlayerListShowPing {
            get { return PlayerListUI?.PlayerListShowPing ?? true; }
            set { if (PlayerListUI != null) PlayerListUI.PlayerListShowPing = value; }
        }

        #endregion

        [SettingSubHeader("modoptions_celestenetclient_subheading_other")]
        public bool EmoteWheel { get; set; } = true;

#region Key Bindings

        [DefaultButtonBinding(Buttons.Back, Keys.Tab)]
        public ButtonBinding ButtonPlayerList { get; set; }

        [DefaultButtonBinding(0, Keys.T)]
        public ButtonBinding ButtonChat { get; set; }

        [SettingSubHeader("modoptions_celestenetclient_binds_playerlist")]
        [DefaultButtonBinding(Buttons.LeftThumbstickUp, Keys.Up)]
        public ButtonBinding ButtonPlayerListScrollUp { get; set; }

        [DefaultButtonBinding(Buttons.LeftThumbstickDown, Keys.Down)]
        public ButtonBinding ButtonPlayerListScrollDown { get; set; }

        [SettingSubHeader("modoptions_celestenetclient_binds_chat")]
        [DefaultButtonBinding(0, Keys.Enter)]
        public ButtonBinding ButtonChatSend { get; set; }

        [DefaultButtonBinding(0, Keys.Escape)]
        public ButtonBinding ButtonChatClose { get; set; }

        [DefaultButtonBinding(Buttons.LeftThumbstickUp, Keys.PageUp)]
        public ButtonBinding ButtonChatScrollUp { get; set; }

        [DefaultButtonBinding(Buttons.LeftThumbstickDown, Keys.PageDown)]
        public ButtonBinding ButtonChatScrollDown { get; set; }

        //[DefaultButtonBinding(0, Keys.Back)]
        //public ButtonBinding ButtonChatBackspace { get; set; }

        //[DefaultButtonBinding(0, Keys.Delete)]
        //public ButtonBinding ButtonChatDelete { get; set; }

        // TODO: Customize ways to open the emote wheel, which stick or maybe even on KB?
        //[DefaultButtonBinding(0, 0)]
        //public ButtonBinding ButtonEmoteWheel { get; set; }

        [SettingSubHeader("modoptions_celestenetclient_binds_emote")]
        [DefaultButtonBinding(Buttons.RightStick, Keys.Q)]
        public ButtonBinding ButtonEmoteWheelSend { get; set; }

        [DefaultButtonBinding(0, Keys.D1)]
        public ButtonBinding ButtonEmote1 { get; set; }

        [DefaultButtonBinding(0, Keys.D2)]
        public ButtonBinding ButtonEmote2 { get; set; }

        [DefaultButtonBinding(0, Keys.D3)]
        public ButtonBinding ButtonEmote3 { get; set; }

        [DefaultButtonBinding(0, Keys.D4)]
        public ButtonBinding ButtonEmote4 { get; set; }

        [DefaultButtonBinding(0, Keys.D5)]
        public ButtonBinding ButtonEmote5 { get; set; }

        [DefaultButtonBinding(0, Keys.D6)]
        public ButtonBinding ButtonEmote6 { get; set; }

        [DefaultButtonBinding(0, Keys.D7)]
        public ButtonBinding ButtonEmote7 { get; set; }

        [DefaultButtonBinding(0, Keys.D8)]
        public ButtonBinding ButtonEmote8 { get; set; }

        [DefaultButtonBinding(0, Keys.D9)]
        public ButtonBinding ButtonEmote9 { get; set; }

        [DefaultButtonBinding(0, Keys.D0)]
        public ButtonBinding ButtonEmote10 { get; set; }

#endregion

        [SettingIgnore]
        public string[] Emotes { get; set; }

#region Helpers

        private float CalcUIScale(int uisize) {
            if (UIScaleOverride > 0f)
                return UIScaleOverride;
            return uisize switch
            {
                1 => 0.25f,
                2 => 0.4f,
                3 => 0.6f,
                4 => 0.75f,
                _ => 0.5f + 0.5f * ((uisize - 1f) / (UISizeMax - 1f)),
            };
        }

        [SettingIgnore, YamlIgnore]
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
        [SettingIgnore, YamlIgnore]
        public int Port {
            get {
                string server = Server;
                int indexOfPort;
                if (!string.IsNullOrEmpty(server) &&
                    (indexOfPort = server.LastIndexOf(':')) != -1 &&
                    int.TryParse(server.Substring(indexOfPort + 1), out int port))
                    return port;

                return 17230;
            }
        }

#endregion

#region Custom Entry Creators

        public void CreateConnectedEntry(TextMenu menu, bool inGame) {
            menu.Add(
                (EnabledEntry = new TextMenu.OnOff("modoptions_celestenetclient_connected".DialogClean(), Connected))
                .Change(v => Connected = v)
            );
            EnabledEntry.AddDescription(menu, "modoptions_celestenetclient_connectedhint".DialogClean());
        }

        public void CreateServerEntry(TextMenu menu, bool inGame) {
#if DEBUG
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
            ServerEntry.AddDescription(menu, "modoptions_celestenetclient_devonlyhint".DialogClean());
#endif
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

        public void CreateUISizeChatEntry(TextMenu menu, bool inGame)
        {
            if (UISizeChat < UISizeMin || UISizeChat > UISizeMax)
                UISizeChat = UISize;
            menu.Add(
                (UISizeChatSlider = new TextMenu.Slider("modoptions_celestenetclient_uisizechat".DialogClean(), i => i.ToString(), UISizeMin, UISizeMax, UISizeChat))
                            .Change(v => _UISizeChat = v)
            );
        }

        public void CreateUISizePlayerListEntry(TextMenu menu, bool inGame)
        {
            if (UISizePlayerList < UISizeMin || UISizePlayerList > UISizeMax)
                UISizePlayerList = UISize;
            menu.Add(
                (UISizePlayerListSlider = new TextMenu.Slider("modoptions_celestenetclient_uisizeplayerlist".DialogClean(), i => i.ToString(), UISizeMin, UISizeMax, UISizePlayerList))
                            .Change(v => _UISizePlayerList = v)
            );
        }
#endregion

        [Flags]
        public enum SyncMode {
            OFF =           0b00,
            Send =          0b01,
            Receive =       0b10,
            ON =            0b11
        }

    }
}
