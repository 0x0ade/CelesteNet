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
    public class CelesteNetClientContext : GameComponent {

        public CelesteNetClient Client;

        public CelesteNetMainComponent Main;
        public CelesteNetRenderHelperComponent RenderHelper;
        public CelesteNetStatusComponent Status;
        public CelesteNetChatComponent Chat;

        public Dictionary<Type, CelesteNetGameComponent> Components = new();
        public List<CelesteNetGameComponent> DrawableComponents;

        // TODO Revert this to Queue<> once MonoKickstart weirdness is fixed
        protected List<Action> MainThreadQueue = new();

        private bool Started = false;
        public readonly bool IsReconnect;

        public static event Action<CelesteNetClientContext> OnCreate;

        public bool IsDisposed { get; private set; }

        public CelesteNetClientContext(Game game, CelesteNetClientContext oldCtx = null)
            : base(game) {

            UpdateOrder = -10000;

            Celeste.Instance.Components.Add(this);

            IsReconnect = oldCtx != null;
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
            Client = new(settings, new() {
                IsReconnect = IsReconnect
            });
            foreach (CelesteNetGameComponent component in Components.Values)
                component.Init();
            OnInit?.Invoke(this);
        }

        public static event Action<CelesteNetClientContext> OnStart;

        public void Start(CancellationToken token) {
            if (Client == null)
                return;

            Client.Start(token);
            foreach (CelesteNetGameComponent component in Components.Values)
                component.Start();

            OnStart?.Invoke(this);

            Started = true;
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            lock (MainThreadQueue) {
                for(int i = 0; i < MainThreadQueue.Count; i++)
                    MainThreadQueue[i]();
                MainThreadQueue.Clear();
            }

            if (Client?.Con != null && Client.SafeDisposeTriggered && Client.Con.IsAlive)
                Client.Con.Dispose();

            if (Started && !(Client?.IsAlive ?? true)) {
                // The connection died
                if (CelesteNetClientModule.Settings.AutoReconnect && CelesteNetClientModule.Settings.WantsToBeConnected) {
                    if (Status.Spin)
                        Status.Set("Disconnected", 3f, false);
                    QueuedTaskHelper.Do("CelesteNetAutoReconnect", 1D, () => CelesteNetClientModule.Instance.Start());
                } else
                    Dispose();
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

        public static event Action<CelesteNetClientContext> OnDispose;

        protected override void Dispose(bool disposing) {
            IsDisposed = true;

            if (CelesteNetClientModule.Instance.Context == this) {
                if (Status.Spin)
                    Status.Set("Disconnected", 3f, false);
                CelesteNetClientModule.Instance.Context = null;
                CelesteNetClientModule.Settings.Connected = false;
            }

            base.Dispose(disposing);

            Client?.Dispose();
            Client = null;

            Celeste.Instance.Components.Remove(this);

            foreach (CelesteNetGameComponent component in Components.Values)
                if (component.Context == this)
                    component.Disconnect();

            OnDispose?.Invoke(this);
        }

    }

    public class ConnectionErrorException : Exception {

        public readonly string Status;

        public ConnectionErrorException(string msg, string status) : base($"{msg}: {status}") {
            Status = status;
        }
    }
}
