using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.CelesteNet.DataTypes;
using FMOD.Studio;
using Monocle;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientModule : EverestModule {

        public static CelesteNetClientModule Instance;

        private static readonly Type? t_OuiDependencyDownloader =
            typeof(Everest).Assembly
            .GetType("Celeste.Mod.UI.OuiDependencyDownloader");
        private static readonly FieldInfo? f_OuiDependencyDownloader_MissingDependencies =
            t_OuiDependencyDownloader?.GetField("MissingDependencies");
        private static readonly MethodInfo? m_Overworld_Goto_OuiDependencyDownloader = 
            t_OuiDependencyDownloader == null ? null :
            typeof(Overworld).GetMethod("Goto")?.MakeGenericMethod(t_OuiDependencyDownloader);

        public override Type SettingsType => typeof(CelesteNetClientSettings);
        public static CelesteNetClientSettings Settings => (CelesteNetClientSettings) Instance._Settings;

        public override Type SessionType => typeof(CelesteNetClientSession);
        public static CelesteNetClientSession Session => (CelesteNetClientSession)Instance._Session;

        public CelesteNetClientContext? ContextLast;
        public CelesteNetClientContext? Context;

        public CelesteNetClientContext? AnyContext => Context ?? ContextLast;
        public CelesteNetClient? Client => Context?.Client;
        private readonly object ClientLock = new();

        private Thread? _StartThread;
        private CancellationTokenSource? _StartTokenSource;
        public bool IsAlive => Context != null;

        public DataDisconnectReason? lastDisconnectReason;

        // simply tracking when the last connection attempt to server was made
        public DateTime LastConnectionAttempt { get; private set; } = DateTime.UtcNow;
        // Repeated connection attempts will incur an incremental delay, this tracks when this started
        public DateTime ReconnectDelayingSince { get; private set; } = DateTime.UtcNow;
        // the current wait time in seconds between connection attempts
        public int ReconnectWaitTime { get; private set; } = 0;
        // wait time increases only happen after X tries at current delay, so track repeats
        public int ReconnectWaitRepetitions { get; private set; } = 0;
        // the delay applied on first reconnect retry
        public const int FastReconnectPenaltyInitial = 2;
        // the highest delay that can be reached
        public const int FastReconnectPenaltyMax = 15;
        // the delay in seconds that gets added at each increment
        public const int FastReconnectPenalty = 3;
        // the amount of retries that happen at each interval before incrementing wait time
        public const int FastReconnectPenaltyAfter = 2;
        // the amount of seconds that need to pass without attempts until delay resets
        public const int FastReconnectResetAfter = 30;

        public bool MayReconnect => (DateTime.UtcNow - LastConnectionAttempt).TotalSeconds > ReconnectWaitTime;

        // currently used to show a warning/reset button to connect to default server again,
        // when connecting to a different server fails repeatedly
        private int _FailedReconnectCount = 0;
        public int FailedReconnectCount {
            get => _FailedReconnectCount;
            private set {
                _FailedReconnectCount = value;

                if (value >= FailedReconnectThreshold && Settings.EffectiveServer != CelesteNetClientSettings.DefaultServer) {
                    Settings.ConnectDefaultVisible = true;
                    Settings.WantsToBeConnected = false;
                }
                else
                    Settings.ConnectDefaultVisible = false;
            }
        }
        public const int FailedReconnectThreshold = 3;

        public VirtualRenderTarget? UIRenderTarget;

        // This should ideally be part of the "emote module" if emotes were a fully separate thing.
        public VirtualJoystick? JoystickEmoteWheel;

        public CelesteNetClientModule() {
            Instance = this;
        }

        public override void Load() {
            Logger.LogCelesteNetTag = true;
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Load");

            // Dirty hackfix for Everest not reloading Monocle debug commands at runtime.
            if (Engine.Commands != null) {
                DynamicData cmds = new(Engine.Commands);
                cmds.Get<IDictionary>("commands")?.Clear();
                cmds.Get<IList>("sorted")?.Clear();
                cmds.Invoke("BuildCommandsList");
            }

            CelesteNetClientRC.Initialize();
            Everest.Events.Celeste.OnShutdown += CelesteNetClientRC.Shutdown;

            CelesteNetClientSpriteDB.Load();
        }

        public override void LoadContent(bool firstLoad) {
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule LoadContent ({firstLoad})");
            MainThreadHelper.Schedule(() => {
                UIRenderTarget?.Dispose();
                UIRenderTarget = VirtualContent.CreateRenderTarget("celestenet-hud-target", 1922, 1082, false, true, 0);
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule LoadContent created RT");
            });
        }

        public override void Unload() {
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Unload");
            CelesteNetClientRC.Shutdown();
            Everest.Events.Celeste.OnShutdown -= CelesteNetClientRC.Shutdown;

            Settings.Connected = false;
            Stop();

            UIRenderTarget?.Dispose();
            UIRenderTarget = null;
        }

        public override void LoadSettings() {
            base.LoadSettings();
            Logger.Log(LogLevel.INF, "LoadSettings", $"Loaded settings with versioning number '{Settings.Version}'.");

            // use newly introduced versioning properties to detect if old settings were loaded
            if (Settings.Version < CelesteNetClientSettings.SettingsVersionCurrent)
            {
                Logger.Log(LogLevel.WRN, "LoadSettings", $"SettingsVersion was {Settings.Version} < {CelesteNetClientSettings.SettingsVersionCurrent}, will load old format and migrate settings...");

                if (LoadOldSettings()) {
                    if (Settings.UISize < CelesteNetClientSettings.UISizeDefault) {
                        Logger.Log(LogLevel.INF, "LoadSettings", $"Settings.UISize was {Settings.UISize} < {CelesteNetClientSettings.UISizeDefault}, performing range adjustments...");

                        if (Settings.UISize < CelesteNetClientSettings.UISizeMin || Settings.UISize > CelesteNetClientSettings.UISizeMax) {
                            Settings.UISize = CelesteNetClientSettings.UISizeDefault;
                            Logger.Log(LogLevel.INF, "LoadSettings", $"Settings.UISize was outside min/max values, set to default of {CelesteNetClientSettings.UISizeDefault}");
                        } else {
                            Logger.Log(LogLevel.VVV, "LoadSettings", $"Found UI Sizes: {Settings.UISize} / {Settings.UISizeChat} / {Settings.UISizePlayerList}");
                            // gotta do it this way because the setter for UISize will also "overwrite" the other two values...
                            int oldSizeChat = Settings.UISizeChat;
                            int oldSizePlayerList = Settings.UISizePlayerList;

                            // just use Settings.UISize if these are outside of range
                            if (oldSizeChat < CelesteNetClientSettings.UISizeMin || oldSizeChat > CelesteNetClientSettings.UISizeMax)
                                oldSizeChat = Settings.UISize;
                            if (oldSizePlayerList < CelesteNetClientSettings.UISizeMin || oldSizePlayerList > CelesteNetClientSettings.UISizeMax)
                                oldSizePlayerList = Settings.UISize;

                            // the adjustments are somewhat arbitrary but the range has been increased from 1 - 4 to 1 - 20 and we don't want to make everyone's UI tiny with the update :)
                            Settings.UISize = Settings.UISize * 2 + 4;
                            Settings.UISizeChat = oldSizeChat * 2 + 4;
                            Settings.UISizePlayerList = oldSizePlayerList * 2 + 4;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(Settings.Name))
                        Settings.Name = "Guest";

                    if (Settings.Name.StartsWith("#")) {
                        Settings.LoginMode = CelesteNetClientSettings.LoginModeType.Key;
                        Settings.Key = Settings.Name;
                        Settings.Name = "Guest";
                    } else {
                        Settings.LoginMode = CelesteNetClientSettings.LoginModeType.Guest;
                        Settings.Key = "";
                    }
                } else {
                    Logger.Log(LogLevel.INF, "LoadSettings", $"Reverting to default settings...");
                    _Settings = new CelesteNetClientSettings();
                }

                Settings.Version = CelesteNetClientSettings.SettingsVersionCurrent;
                Logger.Log(LogLevel.INF, "LoadSettings", $"Settings Migration done, set Version to {Settings.Version}");
            }

            // .ga domains have become inaccessible for some people.
            if (string.IsNullOrWhiteSpace(Settings.Server) || Settings.Server == "celeste.0x0ade.ga" || Settings.Server == "celestenet.0x0ade.ga")
                Settings.Server = CelesteNetClientSettings.DefaultServer;

            // So I did an oopsie, with how the 'Server' property getter was for convenience returning the ServerOverride when it's set,
            // but I had entirely forgotten that this actual getter value will be saved to the settings file... for some reason I didn't think of this
            // and obviously didn't intend it this way.
            // So uhm, for the next month only, reset people who have "Server: localhost" in their yaml? :catplush:
            if (Settings.Server == "localhost" && DateTime.UtcNow.Month < 10 && DateTime.UtcNow.Year == 2024) {
                Settings.Server = CelesteNetClientSettings.DefaultServer;
            }

            if (Settings.Emotes == null || Settings.Emotes.Length == 0) {
                Settings.Emotes = new string[] {
                    "i:collectables/heartgem/0/spin",
                    "i:collectables/strawberry",
                    "Hi!",
                    "Too slow!",
                    "p:madeline/normal04",
                    "p:ghost/scoff03",
                    "p:theo/yolo03 theo/yolo02 theo/yolo01 theo/yolo02 END",
                    "p:granny/laugh",
                };
            }

            if (Settings.ExtraServers == null)
                Settings.ExtraServers = new string[] { };

            if (Settings.ClientID == 0)
                Settings.ClientID = CelesteNetClientSettings.GenerateClientID();

            if (Settings.InstanceID == 0)
                Settings.InstanceID = (uint) DateTime.UtcNow.TimeOfDay.TotalMilliseconds;

            Logger.Log(LogLevel.VVV, "CelesteNetModule", $"ClientID: {Settings.ClientID} InstanceID: {Settings.InstanceID}");
        }

        public bool LoadOldSettings() {

            CelesteNetClientSettingsBeforeVersion2? settingsOld = (CelesteNetClientSettingsBeforeVersion2?) typeof(CelesteNetClientSettingsBeforeVersion2).GetConstructor(Type.EmptyTypes)?.Invoke(Array.Empty<object>());
            string path = UserIO.GetSaveFilePath("modsettings-" + Metadata.Name);

            if (!File.Exists(path)) {
                Logger.Log(LogLevel.WRN, "CelesteNetModule", "Failed to load old settings at " + path);
                return false;
            }

            try {
                using Stream stream = File.OpenRead(path);
                using StreamReader input = new StreamReader(stream);
                YamlHelper.DeserializerUsing(settingsOld!).Deserialize(input, typeof(CelesteNetClientSettingsBeforeVersion2));
            } catch (Exception) {
                Logger.LogDetailed(LogLevel.WRN, "CelesteNetModule", "Failed to load old settings at " + path + " as CelesteNetClientSettingsBeforeVersion2");
                return false;
            }

            if (settingsOld == null) {
                Logger.LogDetailed(LogLevel.WRN, "CelesteNetModule", "Failed to load old settings at " + path + " as CelesteNetClientSettingsBeforeVersion2 (Output object is null)");
                return false;
            } else {
                Settings.AutoReconnect = settingsOld.AutoReconnect;
                Settings.ReceivePlayerAvatars = settingsOld.ReceivePlayerAvatars;

                Settings.Name = settingsOld.Name;
                Settings.Server = settingsOld.Server;

                Settings.Debug.ConnectionType = settingsOld.ConnectionType;
                Settings.Debug.DevLogLevel = settingsOld.DevLogLevel;

                Settings.InGame.Interactions = settingsOld.Interactions;
                Settings.InGame.Sounds = settingsOld.Sounds;
                Settings.InGame.SoundVolume = settingsOld.SoundVolume;
                Settings.InGame.Entities = settingsOld.Entities;
                Settings.InGameHUD.NameOpacity = settingsOld.NameOpacity * 5;
                // this is because of the change to Ghost.OpacityAdjustAlpha(), basically baking in the transformation that used to happen
                Settings.InGame.OtherPlayerOpacity = (int) Math.Round((settingsOld.PlayerOpacity + 2f)/6f * 0.875f * 20);
                Settings.InGameHUD.ShowOwnName = settingsOld.ShowOwnName;

                Settings.PlayerListUI.PlayerListMode = settingsOld.PlayerListMode;
                Settings.UICustomize.PlayerListShortenRandomizer = true;
                Settings.UICustomize.PlayerListAllowSplit = true;
                Settings.PlayerListUI.PlayerListShowPing = settingsOld.PlayerListShowPing;
                Settings.PlayerListUI.ShowPlayerListLocations = settingsOld.ShowPlayerListLocations;

                Settings.ChatUI.ShowNewMessages = settingsOld.ShowNewMessages;
                Settings.ChatUI.ChatLogLength = settingsOld.ChatLogLength;
                Settings.ChatUI.ChatScrollSpeed = settingsOld.ChatScrollSpeed;
                Settings.ChatUI.ChatScrollFading = settingsOld.ChatScrollFading;

                Settings.UIBlur = settingsOld.UIBlur;
                Settings.EmoteWheel = settingsOld.EmoteWheel;

                Settings.ButtonPlayerList = settingsOld.ButtonPlayerList;
                Settings.ButtonChat = settingsOld.ButtonChat;

                Settings.Emotes = settingsOld.Emotes;
            }
            return true;
        }

        public override void OnInputInitialize() {
            base.OnInputInitialize();

            JoystickEmoteWheel = new(true,
                new VirtualJoystick.PadRightStick(Input.Gamepad, 0.2f)
            );
        }

        public override void OnInputDeregister() {
            base.OnInputDeregister();

            JoystickEmoteWheel?.Deregister();
        }

        protected override void CreateModMenuSectionHeader(TextMenu menu, bool inGame, EventInstance snapshot) {
            base.CreateModMenuSectionHeader(menu, inGame, snapshot);

            CelesteNetModShareComponent? sharer = Context?.Get<CelesteNetModShareComponent>();
            if (sharer != null && !inGame) {
                List<EverestModuleMetadata> requested;
                lock (sharer.Requested)
                    requested = new(sharer.Requested);

                if (requested.Count != 0) {
                    TextMenu.Item item;
                    menu.Add(item = new TextMenu.Button("modoptions_celestenetclient_recommended".DialogClean()).Pressed(() => {
                        bool isConnected = Settings.Connected;
                        Settings.Connected = false;
                        f_OuiDependencyDownloader_MissingDependencies?.SetValue(null, requested);
                        m_Overworld_Goto_OuiDependencyDownloader?.Invoke(Engine.Scene, Dummy<object>.EmptyArray);
                        Settings.Connected = isConnected;
                    }));

                    item.AddDescription(
                        menu,
                        "modoptions_celestenetclient_recommendedhint".DialogClean()
                        .Replace("((list))", string.Join(", ", requested.Select(r => r.DLL)))
                    );
                }
            }
        }

        public void Start() {
            // Cancel pending reconnect requests.
            if (Context != null) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: Context wasn't null");
                QueuedTaskHelper.Cancel(new Tuple<object, string>(Context, "CelesteNetAutoReconnect"));
            }

            try {
                _StartTokenSource?.Cancel();
            } catch (ObjectDisposedException) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: STS was disposed");
            }

            if (_StartThread?.IsAlive ?? false) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: StartThread.Join...");
                _StartThread?.Join();
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: StartThread Join done");
            }
            _StartTokenSource?.Dispose();

            lastDisconnectReason = null;

            if (!Settings.WantsToBeConnected) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: WantsToBeConnected has been set to {Settings.WantsToBeConnected}, canceling.");
                return;
            }

            // check if wait time still applies
            if (!MayReconnect) {
                if (Settings.AutoReconnect && Context != null) {
                    Logger.Log(LogLevel.INF, "reconnect-attempt", $"CelesteNetClientModule Start: Waiting {ReconnectWaitTime} seconds before next reconnect...");
                    QueuedTaskHelper.Do(new Tuple<object, string>(Context, "CelesteNetAutoReconnect"), ReconnectWaitTime, () => {
                        Logger.Log(LogLevel.DEV, "reconnect-attempt", $"CelesteNetClientContext - QueueTask: Calling instance Start");
                        Instance.Start();
                    });
                }
                return;
            } else if (++ReconnectWaitRepetitions > FastReconnectPenaltyAfter || ReconnectWaitTime == FastReconnectPenaltyInitial) {
                // increasing penalty after N tries, reset counter
                if (ReconnectWaitTime < FastReconnectPenaltyMax)
                    ReconnectWaitTime += FastReconnectPenalty + Calc.Random.Next(FastReconnectPenalty);
                else
                    ReconnectWaitTime = FastReconnectPenaltyMax;
                ReconnectWaitRepetitions = 0;
            }
            
            // fully reset wait time
            if ((DateTime.UtcNow - LastConnectionAttempt).TotalSeconds > FastReconnectResetAfter && ReconnectWaitTime > 0) {
                ResetReconnectPenalty();
            }

            lock (ClientLock) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: old ctx: {Context} {Context?.IsDisposed}");
                CelesteNetClientContext? oldCtx = Context;
                if (oldCtx?.IsDisposed ?? false)
                    oldCtx = null;
                Context = new(Celeste.Instance, oldCtx);
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: old ctx: {oldCtx} / new {Context}");
                oldCtx?.DisposeSafe(true);

                oldCtx = ContextLast;
                if (oldCtx != null) {
                    Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: ContextLast wasn't null");
                    MainThreadHelper.Schedule(() => {
                        foreach (CelesteNetGameComponent comp in oldCtx.Components.Values)
                            comp.Disconnect(true);
                    });
                    ContextLast = null;
                }

                Context?.Status?.Set("Initializing...");
            }

            LastConnectionAttempt = DateTime.UtcNow;

            // ReconnectDelayingSince shall track the time when connection attempts started, prior to any delays
            if (ReconnectWaitTime == 0) {
                Logger.Log(LogLevel.DBG, "reconnect-attempt", $"CelesteNetClientModule Start: Setting initial reconnect delay from {ReconnectWaitTime} seconds to {FastReconnectPenaltyInitial}... (started {ReconnectDelayingSince})");
                ReconnectDelayingSince = DateTime.UtcNow;
                ReconnectWaitTime = FastReconnectPenaltyInitial + Calc.Random.Next(FastReconnectPenaltyInitial);
            }

            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: Creating StartThread...");
            _StartThread = new(() => {
                CelesteNetClientContext? context = Context;
                try {
                    // This shouldn't ever happen but it has happened once.
                    if (context == null) {
                        Logger.Log(LogLevel.WRN, "main", $"CelesteNetClientModule StartThread context: 'This shouldn't ever happen but it has happened once.'");
                        return;
                    }
                    if (_StartTokenSource == null) {
                        Logger.Log(LogLevel.WRN, "main", $"CelesteNetClientModule StartThread _StartTokenSource: 'This shouldn't ever happen but it has happened once.'");
                        return;
                    }

                    context.Init(Settings);
                    context.Status?.Set("Connecting...");

                    using (_StartTokenSource) {
                        Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule StartThread: Going into context Start...");
                        context.Start(_StartTokenSource.Token);
                    }
                    if (context.Status?.Spin ?? false)
                        context.Status?.Set("Connected", 1f);

                    FailedReconnectCount = 0;
                } catch (Exception e) when (e is ThreadInterruptedException || e is OperationCanceledException) {
                    Logger.Log(LogLevel.CRI, "clientmod", "Startup interrupted.");
                    _StartThread = null;
                    Stop();
                    context?.Status?.Set("Interrupted", 3f, false);
                } catch (ThreadAbortException) {
                    Logger.Log(LogLevel.VVV, "main", $"Client Start thread: ThreadAbortException caught");
                    _StartThread = null;
                    Stop();

                } catch (Exception e) {
                    bool handled = false;
                    for (Exception? ie = e; ie != null; ie = ie.InnerException) {
                        if (ie is ConnectionErrorCodeException ceee) {
                            Logger.Log(LogLevel.CRI, "clientmod", $"Connection error:\n{e}");
                            _StartThread = null;
                            Stop();
                            AnyContext?.Status?.Set(ceee.Status ?? "Connection failed", 3f, false);
                            Settings.KeyError = CelesteNetClientSettings.KeyErrors.None;
                            if (ceee.StatusCode == 403)
                                Settings.KeyError = CelesteNetClientSettings.KeyErrors.InvalidKey;
                            handled = true;
                            break;
                        }
                    }

                    if (!handled) {
                        Logger.Log(LogLevel.CRI, "clientmod", $"Failed connecting:\n{e}");
                        // Don't stop the context on unhandled connection errors so that it gets a chance to retry.
                        // Instead, dispose the client and let the context do the rest.
                        if (context?.Client != null)
                            context.Client.SafeDisposeTriggered = true;
                        context?.Status?.Set("Connection failed", 3f, false);
                        FailedReconnectCount++;
                    }

                } finally {
                    _StartThread = null;
                    _StartTokenSource = null;
                    LastConnectionAttempt = DateTime.UtcNow;
                }
            }) {
                Name = "CelesteNet Client Start",
                IsBackground = true
            };
            _StartTokenSource = new();
            _StartThread.Start();
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: done");
        }

        public void Stop() {
            if (Context != null) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: Context wasn't null");
                QueuedTaskHelper.Cancel(new Tuple<object, string>(Context, "CelesteNetAutoReconnect"));
            }

            try {
                _StartTokenSource?.Cancel();
            } catch (ObjectDisposedException) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: StartTokenSource was disposed");
            }

            if (_StartThread?.IsAlive ?? false) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: Joining StartThread...");
                _StartThread.Join();
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: Joining done");
            }
            _StartTokenSource?.Dispose();

            lock (ClientLock) {
                if (Context == null) {
                    Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: Context was null already, returning");
                    return;
                }

                ContextLast = Context;
                Context.DisposeSafe();
                Context = null;
            }
        }

        public static Type[] GetTypes() {
            if (Everest.Modules.Count != 0)
                return _GetTypes();

            Type[] typesPrev = _GetTypes();
        Retry:
            Type[] types = _GetTypes();
            if (typesPrev.Length != types.Length) {
                typesPrev = types;
                goto Retry;
            }
            return types;
        }

        private static IEnumerable<Assembly> _GetAssemblies()
            => (Everest.Modules?.Select(m => m.GetType().Assembly) ?? new Assembly[0])
            .Concat(AppDomain.CurrentDomain.GetAssemblies())
            .Distinct();

        private static Type[] _GetTypes()
            => _GetAssemblies().SelectMany(_GetTypes).ToArray();

        private static IEnumerable<Type> _GetTypes(Assembly asm) {
            try {
                return asm.GetTypes();
            } catch (ReflectionTypeLoadException e) {
#pragma warning disable CS8619 // Compiler thinks this could be <Type?> even though we check for t != null
                return e.Types.Where(t => t != null);
#pragma warning restore CS8619
            }
        }

        public void ResetReconnectPenalty() {
            Logger.Log(LogLevel.INF, "reconnect-attempt", $"CelesteNetClientModule Start: Resetting reconnect delay from {ReconnectWaitTime} seconds to 0... (started {ReconnectDelayingSince})");
            ReconnectWaitTime = 0;
            ReconnectWaitRepetitions = 0;
        }

    }
}
