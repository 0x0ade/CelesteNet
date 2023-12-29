using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using FMOD.Studio;
using MonoMod.Utils;
using System.Collections;
using Celeste.Mod.CelesteNet.Client.Components;
using System.IO;
using Celeste.Mod.CelesteNet.Client.Utils;
using Celeste.Mod.Helpers;
using System.Threading.Tasks;
using MonoMod.Cil;

namespace Celeste.Mod.CelesteNet.Client
{
    public class CelesteNetClientModule : EverestModule
    {

        public static CelesteNetClientModule Instance;

        private static readonly Type t_OuiDependencyDownloader =
            typeof(Everest).Assembly
            .GetType("Celeste.Mod.UI.OuiDependencyDownloader");
        private static readonly FieldInfo f_OuiDependencyDownloader_MissingDependencies =
            t_OuiDependencyDownloader.GetField("MissingDependencies");
        private static readonly MethodInfo m_Overworld_Goto_OuiDependencyDownloader =
            typeof(Overworld).GetMethod("Goto")
            .MakeGenericMethod(t_OuiDependencyDownloader);

        public override Type SettingsType => typeof(CelesteNetClientSettings);
        public static CelesteNetClientSettings Settings => (CelesteNetClientSettings)Instance._Settings;

        public CelesteNetClientContext ContextLast;
        public CelesteNetClientContext Context;
        public CelesteNetClient Client => Context?.Client;
        private readonly object ClientLock = new();

        private Thread _StartThread;
        private CancellationTokenSource _StartTokenSource;
        public bool IsAlive => Context != null;

        public VirtualRenderTarget UIRenderTarget;

        // This should ideally be part of the "emote module" if emotes were a fully separate thing.
        public VirtualJoystick JoystickEmoteWheel;
        public EverestModule CelesteNetModule;

        // 官服检测
        // 是否又安装了官服
        public bool InstalledBothServer { get; set; }

        // 是否已经显示过了警告
        public bool ShownInstalledBothServerWarning { get; set; }

        public string CurrentVersion { get; set; }
        public string CurrentCelesteNetVersion { get; set; }

        public CelesteNetClientModule()
        {
            Instance = this;
        }

        public override void Load()
        {
            Logger.LogCelesteNetTag = true;
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Load");

            // Dirty hackfix for Everest not reloading Monocle debug commands at runtime.
            if (Engine.Commands != null)
            {
                DynamicData cmds = new(Engine.Commands);
                cmds.Get<IDictionary>("commands").Clear();
                cmds.Get<IList>("sorted").Clear();
                cmds.Invoke("BuildCommandsList");
            }

            CelesteNetClientRC.Initialize();
            Everest.Events.Celeste.OnShutdown += CelesteNetClientRC.Shutdown;

            if (Everest.Loader.TryGetDependency(new() { Name = "CelesteNet.Client", Version = new(2, 0, 0) }, out var module))
            {
                InstalledBothServer = true;
                CurrentCelesteNetVersion = module.Metadata.VersionString;
            }
            if (Everest.Loader.TryGetDependency(new() { Name = "Miao.CelesteNet.Client", Version = new(3, 0, 0) }, out var moduleMiao))
            {
                CurrentVersion = moduleMiao.Metadata.VersionString;
            }

            CelesteNetClientSpriteDB.Load();
            CelesteNetModule = (EverestModule)Activator.CreateInstance(FakeAssembly.GetFakeEntryAssembly().GetType("Celeste.Mod.NullModule"), new EverestModuleMetadata
            {
                Name = "CelesteNet.Client",
                VersionString = "2.2.2"
            });
            Everest.Register(CelesteNetModule);
            On.Celeste.OuiMainMenu.Enter += OuiMainMenu_Enter;
        }

        private IEnumerator OuiMainMenu_Enter(On.Celeste.OuiMainMenu.orig_Enter orig, OuiMainMenu self, Oui from)
        {
            if (InstalledBothServer && !ShownInstalledBothServerWarning)
            {
                self.Overworld.Goto<OuiBothServerInstalledWhoops>();
            }
            else
            {
                yield return new SwapImmediately(orig(self, from));
                if (Settings.AutoConnect)
                {
                    Task.Delay(3000).ContinueWith(t =>
                    {
                        Settings.Connected = true;
                    });
                }
            }
        }

        public override void LoadContent(bool firstLoad)
        {
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule LoadContent ({firstLoad})");
            MainThreadHelper.Do(() =>
            {
                UIRenderTarget?.Dispose();
                UIRenderTarget = VirtualContent.CreateRenderTarget("celestenet-hud-target", 1922, 1082, false, true, 0);
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule LoadContent created RT");
            });
        }

        public override void Unload()
        {
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Unload");
            CelesteNetClientRC.Shutdown();
            Everest.Events.Celeste.OnShutdown -= CelesteNetClientRC.Shutdown;

            Settings.Connected = false;
            Stop();

            UIRenderTarget?.Dispose();
            UIRenderTarget = null;

            On.Celeste.OuiMainMenu.Enter -= OuiMainMenu_Enter;
        }

        public override void LoadSettings()
        {
            base.LoadSettings();
            Logger.Log(LogLevel.INF, "LoadSettings", $"Loaded settings with versioning number '{Settings.Version}'.");

            // use newly introduced versioning properties to detect if old settings were loaded
            if (Settings.Version < CelesteNetClientSettings.SettingsVersionCurrent)
            {
                Logger.Log(LogLevel.WRN, "LoadSettings", $"SettingsVersion was {Settings.Version} < {CelesteNetClientSettings.SettingsVersionCurrent}, will load old format and migrate settings...");

                if (LoadOldSettings())
                {
                    if (Settings.UISize < CelesteNetClientSettings.UISizeDefault)
                    {
                        Logger.Log(LogLevel.INF, "LoadSettings", $"Settings.UISize was {Settings.UISize} < {CelesteNetClientSettings.UISizeDefault}, performing range adjustments...");

                        if (Settings.UISize < CelesteNetClientSettings.UISizeMin || Settings.UISize > CelesteNetClientSettings.UISizeMax)
                        {
                            Settings.UISize = CelesteNetClientSettings.UISizeDefault;
                            Logger.Log(LogLevel.INF, "LoadSettings", $"Settings.UISize was outside min/max values, set to default of {CelesteNetClientSettings.UISizeDefault}");
                        }
                        else
                        {
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

                }
                else
                {
                    Logger.Log(LogLevel.INF, "LoadSettings", $"Reverting to default settings...");
                    _Settings = new CelesteNetClientSettings();
                }

                Settings.Version = CelesteNetClientSettings.SettingsVersionCurrent;
                Logger.Log(LogLevel.INF, "LoadSettings", $"Settings Migration done, set Version to {Settings.Version}");
            }

            Settings.Server = CelesteNetClientSettings.DefaultServer;

            if (Settings.Emotes == null || Settings.Emotes.Length == 0)
            {
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
                Settings.InstanceID = (uint)DateTime.UtcNow.TimeOfDay.TotalMilliseconds;

            Logger.Log(LogLevel.VVV, "CelesteNetModule", $"ClientID: {Settings.ClientID} InstanceID: {Settings.InstanceID}");
        }

        public bool LoadOldSettings()
        {

            CelesteNetClientSettingsBeforeVersion2 settingsOld = (CelesteNetClientSettingsBeforeVersion2)typeof(CelesteNetClientSettingsBeforeVersion2).GetConstructor(Everest._EmptyTypeArray).Invoke(Everest._EmptyObjectArray);
            string path = UserIO.GetSaveFilePath("modsettings-" + Metadata.Name);

            if (!File.Exists(path))
            {
                Logger.Log(LogLevel.WRN, "CelesteNetModule", "Failed to load old settings at " + path);
                return false;
            }

            try
            {
                using Stream stream = File.OpenRead(path);
                using StreamReader input = new StreamReader(stream);
                YamlHelper.DeserializerUsing(settingsOld).Deserialize(input, typeof(CelesteNetClientSettingsBeforeVersion2));
            }
            catch (Exception)
            {
                Logger.LogDetailed(LogLevel.WRN, "CelesteNetModule", "Failed to load old settings at " + path + " as CelesteNetClientSettingsBeforeVersion2");
                return false;
            }

            if (settingsOld == null)
            {
                Logger.LogDetailed(LogLevel.WRN, "CelesteNetModule", "Failed to load old settings at " + path + " as CelesteNetClientSettingsBeforeVersion2 (Output object is null)");
                return false;
            }
            else
            {
                Settings.AutoReconnect = settingsOld.AutoReconnect;
                Settings.ReceivePlayerAvatars = settingsOld.ReceivePlayerAvatars;

                Settings.Server = settingsOld.Server;

                Settings.Debug.ConnectionType = settingsOld.ConnectionType;
                Settings.Debug.DevLogLevel = settingsOld.DevLogLevel;

                Settings.InGame.Interactions = settingsOld.Interactions;
                Settings.InGame.Sounds = settingsOld.Sounds;
                Settings.InGame.SoundVolume = settingsOld.SoundVolume;
                Settings.InGame.Entities = settingsOld.Entities;
                Settings.InGameHUD.NameOpacity = settingsOld.NameOpacity * 5;
                // this is because of the change to Ghost.OpacityAdjustAlpha(), basically baking in the transformation that used to happen
                Settings.InGame.OtherPlayerOpacity = (int)Math.Round((settingsOld.PlayerOpacity + 2f) / 6f * 0.875f * 20);
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

        public override void OnInputInitialize()
        {
            base.OnInputInitialize();

            JoystickEmoteWheel = new(true,
                new VirtualJoystick.PadRightStick(Input.Gamepad, 0.2f)
            );
        }

        public override void OnInputDeregister()
        {
            base.OnInputDeregister();

            JoystickEmoteWheel?.Deregister();
        }

        protected override void CreateModMenuSectionHeader(TextMenu menu, bool inGame, EventInstance snapshot)
        {
            base.CreateModMenuSectionHeader(menu, inGame, snapshot);

            CelesteNetModShareComponent sharer = Context?.Get<CelesteNetModShareComponent>();
            if (sharer != null && !inGame)
            {
                List<EverestModuleMetadata> requested;
                lock (sharer.Requested)
                    requested = new(sharer.Requested);

                if (requested.Count != 0)
                {
                    TextMenu.Item item;
                    menu.Add(item = new TextMenu.Button("modoptions_celestenetclient_recommended".DialogClean()).Pressed(() =>
                    {
                        bool isConnected = Settings.Connected;
                        Settings.Connected = false;
                        f_OuiDependencyDownloader_MissingDependencies.SetValue(null, requested);
                        m_Overworld_Goto_OuiDependencyDownloader.Invoke(Engine.Scene, Dummy<object>.EmptyArray);
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

        public void Start()
        {
            // Cancel pending reconnect requests.
            if (Context != null)
            {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: Context wasn't null");
                QueuedTaskHelper.Cancel(new Tuple<object, string>(Context, "CelesteNetAutoReconnect"));
            }

            try
            {
                _StartTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: STS was disposed");
            }

            if (_StartThread?.IsAlive ?? false)
            {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: StartThread.Join...");
                _StartThread.Join();
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: StartThread Join done");
            }
            _StartTokenSource?.Dispose();

            lock (ClientLock)
            {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: old ctx: {Context} {Context?.IsDisposed}");
                CelesteNetClientContext oldCtx = Context;
                if (oldCtx?.IsDisposed ?? false)
                    oldCtx = null;
                Context = new(Celeste.Instance, oldCtx);
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: old ctx: {oldCtx} / new {Context}");
                oldCtx?.DisposeSafe(true);

                oldCtx = ContextLast;
                if (oldCtx != null)
                {
                    Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: ContextLast wasn't null");
                    MainThreadHelper.Do(() =>
                    {
                        foreach (CelesteNetGameComponent comp in oldCtx.Components.Values)
                            comp.Disconnect(true);
                    });
                    ContextLast = null;
                }

                Context.Status.Set("Initializing...");
            }

            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: Creating StartThread...");
            _StartThread = new(() =>
            {
                CelesteNetClientContext context = Context;
                try
                {
                    // This shouldn't ever happen but it has happened once.
                    if (context == null)
                    {
                        Logger.Log(LogLevel.WRN, "main", $"CelesteNetClientModule StartThread: 'This shouldn't ever happen but it has happened once.'");
                        return;
                    }
                    context.Init(Settings);
                    if (String.IsNullOrEmpty(CelesteNetClientModule.Settings.RefreshToken) && String.IsNullOrEmpty(Settings.Key))
                    {
                        context.Status.Set("Please login first", 3f);
                        Thread.Sleep(3000);
                        _StartThread = null;
                        Stop();
                        return;
                    }

                    if (!String.IsNullOrEmpty(Settings.RefreshToken) && Settings.ExpiredTime != null)
                    {
                        System.DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1)); //
                        long timeStamp = (long)(DateTime.Now - startTime).TotalSeconds;
                        if (timeStamp > Convert.ToInt64(Settings.ExpiredTime))
                        {
                            if (!TokenUtils.refreshToken())
                            {
                                context.Status.Set("Please login again", 3f);
                                Thread.Sleep(3000);
                                _StartThread = null;
                                Stop();
                                return;
                            }
                        }
                    }
                    context.Status.Set("Connecting...");
                    using (_StartTokenSource)
                    {
                        Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule StartThread: Going into context Start...");
                        context.Start(_StartTokenSource.Token);
                    }
                    if (context.Status.Spin)
                        context.Status.Set("Connected", 1f);

                }
                catch (Exception e) when (e is ThreadInterruptedException || e is OperationCanceledException)
                {
                    Logger.Log(LogLevel.CRI, "clientmod", "Startup interrupted.");
                    _StartThread = null;
                    Stop();
                    context.Status.Set("Interrupted", 3f, false);

                }
                catch (ThreadAbortException)
                {
                    Logger.Log(LogLevel.VVV, "main", $"Client Start thread: ThreadAbortException caught");
                    _StartThread = null;
                    Stop();

                }
                catch (Exception e)
                {
                    bool handled = false;
                    for (Exception ie = e; ie != null; ie = ie.InnerException)
                    {
                        if (ie is ConnectionErrorException cee)
                        {
                            Logger.Log(LogLevel.CRI, "clientmod", $"Connection error:\n{e}");
                            _StartThread = null;
                            Stop();
                            context.Status.Set(cee.Status ?? "Connection failed", 3f, false);
                            handled = true;
                            break;
                        }
                    }

                    if (!handled)
                    {
                        Logger.Log(LogLevel.CRI, "clientmod", $"Failed connecting:\n{e}");
                        // Don't stop the context on unhandled connection errors so that it gets a chance to retry.
                        // Instead, dispose the client and let the context do the rest.
                        context.Client.SafeDisposeTriggered = true;
                        context.Status.Set("Connection failed", 3f, false);
                    }

                }
                finally
                {
                    _StartThread = null;
                    _StartTokenSource = null;
                }
            })
            {
                Name = "CelesteNet Client Start",
                IsBackground = true
            };
            _StartTokenSource = new();
            _StartThread.Start();
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Start: done");
        }

        public void Stop()
        {
            if (Context != null)
            {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: Context wasn't null");
                QueuedTaskHelper.Cancel(new Tuple<object, string>(Context, "CelesteNetAutoReconnect"));
            }

            try
            {
                _StartTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: StartTokenSource was disposed");
            }

            if (_StartThread?.IsAlive ?? false)
            {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: Joining StartThread...");
                _StartThread.Join();
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: Joining done");
            }
            _StartTokenSource?.Dispose();

            lock (ClientLock)
            {
                if (Context == null)
                {
                    Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientModule Stop: Context was null already, returning");
                    return;
                }

                ContextLast = Context;
                Context.DisposeSafe();
                Context = null;
            }
        }

    }
}
