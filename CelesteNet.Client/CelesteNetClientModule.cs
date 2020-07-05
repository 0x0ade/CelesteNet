using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FMOD.Studio;
using MonoMod.Utils;
using System.Collections;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientModule : EverestModule {

        public static CelesteNetClientModule Instance;

        public override Type SettingsType => typeof(CelesteNetClientSettings);
        public static CelesteNetClientSettings Settings => (CelesteNetClientSettings) Instance._Settings;

        public CelesteNetClientComponent ContextLast;
        public CelesteNetClientComponent Context;
        public CelesteNetClient Client => Context?.Client;
        private readonly object ClientLock = new object();

        private Thread _StartThread;
        public bool IsAlive => Context != null;

        // This should ideally be part of the "emote module" if emotes were a fully separate thing.
        public VirtualJoystick JoystickEmoteWheel;

        public CelesteNetClientModule() {
            Instance = this;
        }

        public override void Load() {
            Logger.LogCelesteNetTag = true;

            // Dirty hackfix for Everest not reloading Monocle debug commands at runtime.
            if (Engine.Commands != null) {
                DynamicData cmds = new DynamicData(Engine.Commands);
                cmds.Get<IDictionary>("commands").Clear();
                cmds.Get<IList>("sorted").Clear();
                cmds.Invoke("BuildCommandsList");
            }

            CelesteNetClientRC.Initialize();
            Everest.Events.Celeste.OnShutdown += CelesteNetClientRC.Shutdown;
        }

        public override void Unload() {
            CelesteNetClientRC.Shutdown();
            Everest.Events.Celeste.OnShutdown -= CelesteNetClientRC.Shutdown;

            Settings.Connected = false;
            Stop();
        }

        public override void LoadSettings() {
            base.LoadSettings();

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
        }

        public override void OnInputInitialize() {
            base.OnInputInitialize();

            JoystickEmoteWheel = new VirtualJoystick(true,
                new VirtualJoystick.PadRightStick(Input.Gamepad, 0.2f)
            );
        }

        public override void OnInputDeregister() {
            base.OnInputDeregister();

            JoystickEmoteWheel?.Deregister();
        }

        public void Start() {
            if (_StartThread?.IsAlive ?? false)
                _StartThread.Join();

            lock (ClientLock) {
                CelesteNetClientComponent last = Context ?? ContextLast;
                if (Client?.IsAlive ?? false)
                    Stop();

                last?.Status?.Set(null);

                Context = new CelesteNetClientComponent(Celeste.Instance);
                ContextLast = Context;

                Context.Status.Set("Initializing...");
            }

            _StartThread = new Thread(() => {
                CelesteNetClientComponent context = Context;
                try {
                    context.Init(Settings);
                    context.Status.Set("Connecting...");
                    context.Start();
                    if (context.Status.Spin)
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

        public void Stop() {
            QueuedTaskHelper.Cancel("CelesteNetAutoReconnect");

            if (_StartThread?.IsAlive ?? false)
                _StartThread.Join();

            lock (ClientLock) {
                if (Context == null)
                    return;

                ContextLast = Context;
                Context.Dispose();
                Context = null;
            }
        }

    }
}
