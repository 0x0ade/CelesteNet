using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientContext : DrawableGameComponent {

        public CelesteNetClient Client;

        public CelesteNetMainComponent Main;
        public CelesteNetRenderHelperComponent RenderHelper;
        public CelesteNetStatusComponent Status;
        public CelesteNetChatComponent Chat;

        public Dictionary<Type, CelesteNetGameComponent> Components = new();
        public List<CelesteNetGameComponent> DrawableComponents;

        // TODO Revert this to Queue<> once MonoKickstart weirdness is fixed
        protected List<Action> MainThreadQueue = new();

        private bool Started;
        private bool Reconnecting;
        public bool Succeeded;

        public readonly bool IsReconnect;

        public static event Action<CelesteNetClientContext> OnCreate;

        public bool IsDisposed { get; private set; }

        private readonly object DisposeLock = new();
        private bool CoreDisposed;
        private bool SafeDisposeTriggered, SafeDisposeForceDispose;

        public CelesteNetClientContext(Game game, CelesteNetClientContext oldCtx = null)
            : base(game) {

            UpdateOrder = -10000;
            DrawOrder = int.MaxValue;

            Celeste.Instance.Components.Add(this);

            IsReconnect = oldCtx?.Succeeded ?? false;
            if (oldCtx != null)
                foreach (CelesteNetGameComponent comp in oldCtx.Components.Values) {
                    if (!comp.Persistent)
                        continue;

                    // "Recycle" persistent components
                    comp.Reconnect(this);
                    Logger.Log(LogLevel.INF, "clientcomp", $"Recycled component: {comp}");
                    Components[comp.GetType()] = comp;
                }

            foreach (Type type in FakeAssembly.GetFakeEntryAssembly().GetTypes()) {
                if (type.IsAbstract || !typeof(CelesteNetGameComponent).IsAssignableFrom(type) || Components.ContainsKey(type))
                    continue;

                Add((CelesteNetGameComponent) Activator.CreateInstance(type, this, game));
            }

            Main = Get<CelesteNetMainComponent>();
            RenderHelper = Get<CelesteNetRenderHelperComponent>();
            Status = Get<CelesteNetStatusComponent>();
            Chat = Get<CelesteNetChatComponent>();
            DrawableComponents = Components.Values.Where(c => c.Visible).OrderBy(c => c.DrawOrder).ToList();

            OnCreate?.Invoke(this);
        }
        protected void Add(CelesteNetGameComponent component) {
            Logger.Log(LogLevel.INF, "clientcomp", $"Added component: {component}");
            Components[component.GetType()] = component;
            Game.Components.Add(component);
        }

        public T Get<T>() where T : CelesteNetGameComponent
            => Components.TryGetValue(typeof(T), out CelesteNetGameComponent component) ? (T) component : null;

        public static event Action<CelesteNetClientContext> OnInit;

        public void Init(CelesteNetClientSettings settings) {
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Init: Creating client");
            Client = new(settings, new() {
                IsReconnect = IsReconnect
            });
            foreach (CelesteNetGameComponent component in Components.Values) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Init: Init comp {component.GetType()}");
                component.Init();
            }
            OnInit?.Invoke(this);
        }

        public static event Action<CelesteNetClientContext> OnStart;

        public void Start(CancellationToken token) {
            Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Start: ");

            if (Client == null) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Start: Client was null, returning");
                return;
            }

            Started = true;

            Client.Start(token);
            foreach (CelesteNetGameComponent component in Components.Values)
                component.Start();

            OnStart?.Invoke(this);

            Succeeded = true;
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            lock (MainThreadQueue) {
                for (int i = 0; i < MainThreadQueue.Count; i++)
                    MainThreadQueue[i]();
                MainThreadQueue.Clear();
            }

            if ((Client?.SafeDisposeTriggered ?? false) && (Client?.IsAlive ?? false)) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Update: Disposing client because Client.SafeDisposeTriggered");
                Client.Dispose();
            }
        }

        public override void Draw(GameTime gameTime) {
            base.Draw(gameTime);

            // This must happen at the very end, as XNA / FNA won't update their internal lists, causing "dead" components to update.

            if (SafeDisposeTriggered) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Draw: Disposing because SafeDisposeTriggered");
                if (!IsDisposed)
                    Dispose();
                return;
            }

            if (Started && !(Client?.IsAlive ?? false)) {
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Draw: Client is dead? {Client} {Client?.IsAlive}");
                // The connection died.
                if (CelesteNetClientModule.Settings.AutoReconnect && CelesteNetClientModule.Settings.WantsToBeConnected) {
                    if (!Reconnecting) {
                        Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Draw: Initiating auto-reconnect");
                        Reconnecting = true;
                        // FIXME: Make sure that nothing tries to make use of the dead connection until the restart.
                        if (Status.Spin)
                            Status.Set("Disconnected", 3f, false);
                        QueuedTaskHelper.Do(new Tuple<object, string>(this, "CelesteNetAutoReconnect"), 2D, () => {
                            if (CelesteNetClientModule.Instance.Context == this) {
                                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext - QueueTask: Calling instance Start");
                                CelesteNetClientModule.Instance.Start();
                            }
                        });
                    }
                } else {
                    Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Draw: No auto-recon - disposing");
                    Dispose();
                }
            }
        }

        internal void _RunOnMainThread(Action action, bool wait = false)
            => RunOnMainThread(action, wait);
        protected void RunOnMainThread(Action action, bool wait = false) {
            if (Thread.CurrentThread == MainThreadHelper.MainThread) {
                action();
                return;
            }

            using ManualResetEvent waiter = wait ? new ManualResetEvent(false) : null;
            if (wait) {
                Action real = action;
                action = () => {
                    try {
                        real();
                    } finally {
                        waiter.Set();
                    }
                };
            }

            lock (MainThreadQueue)
                MainThreadQueue.Add(action);

            if (wait)
                WaitHandle.WaitAny(new WaitHandle[] { waiter });
        }
        
        protected void DisposeCore() {
            lock (DisposeLock) {
                if (CoreDisposed)
                    return;
                CoreDisposed = true;
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext DisposeCore called");

                if (CelesteNetClientModule.Instance.Context == this) {
                    Status.Set("Disconnected", 3f, false);
                    CelesteNetClientModule.Instance.Context = null;
                    CelesteNetClientModule.Settings.Connected = false;
                }

                Client?.Dispose();
                Client = null;
            }
        }

        public void DisposeSafe(bool forceDispose = false) {
            lock (DisposeLock) {
                if (IsDisposed)
                    return;
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext DisposeSafe called");

                SafeDisposeForceDispose |= forceDispose;
                SafeDisposeTriggered = true;

                DisposeCore();
            }
        }

        public static event Action<CelesteNetClientContext> OnDispose;

        protected override void Dispose(bool disposing) {
            lock (DisposeLock) {
                if (IsDisposed)
                    return;
                IsDisposed = true;
                Logger.Log(LogLevel.DEV, "lifecycle", $"CelesteNetClientContext Dispose called");

                base.Dispose(disposing);

                DisposeCore();

                Celeste.Instance.Components.Remove(this);

                foreach (CelesteNetGameComponent component in Components.Values)
                    if (component.Context == this)
                        component.Disconnect(SafeDisposeForceDispose);

                OnDispose?.Invoke(this);
            }
        }

    }

    public class ConnectionErrorException : Exception {

        public readonly string Status;

        public ConnectionErrorException(string msg, string status) : base($"{msg}: {status}") {
            Status = status;
        }
    }
}
