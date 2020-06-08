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

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientModule : EverestModule {

        public static CelesteNetClientModule Instance;

        public override Type SettingsType => typeof(CelesteNetClientSettings);
        public static CelesteNetClientSettings Settings => (CelesteNetClientSettings) Instance._Settings;

        public CelesteNetClientComponent ContextLast;
        public CelesteNetClientComponent Context;
        public CelesteNetClient Client => Context?.Client;
        private object ClientLock = new object();

        private Thread _StartThread;
        public bool IsAlive => Context != null;

        // This should ideally be part of the "emote module" if emotes were a fully separate thing.
        public VirtualJoystick JoystickEmoteWheel;
        public VirtualButton ButtonEmoteSend;

        public CelesteNetClientModule() {
            Instance = this;
        }

        public override void Load() {
            Logger.LogCelesteNetTag = true;
        }

        public override void Unload() {
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
            ButtonEmoteSend = new VirtualButton(
                new VirtualButton.KeyboardKey(Keys.Q),
                new VirtualButton.PadButton(Input.Gamepad, Buttons.RightStick)
            );
        }

        public override void OnInputDeregister() {
            base.OnInputDeregister();

            JoystickEmoteWheel?.Deregister();
            ButtonEmoteSend?.Deregister();
        }


        public void Start() {
            lock (ClientLock) {
                if (_StartThread?.IsAlive ?? false)
                    _StartThread.Join();

                CelesteNetClientComponent last = Context ?? ContextLast;
                if (Client?.IsAlive ?? false)
                    Stop();

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
                    _StartThread.Join();

                if (Context == null)
                    return;

                ContextLast = Context;
                Context.Dispose();
                Context = null;
            }
        }

    }
}
