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

        public Dictionary<Type, CelesteNetGameComponent> Components = new Dictionary<Type, CelesteNetGameComponent>();
        public List<CelesteNetGameComponent> DrawableComponents;

        protected Queue<Action> MainThreadQueue = new Queue<Action>();

        private bool Started = false;

        public static event Action<CelesteNetClientContext> OnCreate;

        public bool IsDisposed { get; private set; }

        public CelesteNetClientContext(Game game)
            : base(game) {

            UpdateOrder = -10000;

            Celeste.Instance.Components.Add(this);

            Add(Main = new CelesteNetMainComponent(this, game));
            Add(RenderHelper = new CelesteNetRenderHelperComponent(this, game));
            Add(Status = new CelesteNetStatusComponent(this, game));
            Add(Chat = new CelesteNetChatComponent(this, game));

            foreach (Type type in FakeAssembly.GetFakeEntryAssembly().GetTypes()) {
                if (type.IsAbstract || !typeof(CelesteNetGameComponent).IsAssignableFrom(type) || Components.ContainsKey(type))
                    continue;

                Add((CelesteNetGameComponent) Activator.CreateInstance(type, this, game));
            }

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
            Client = new CelesteNetClient(settings);
            foreach (CelesteNetGameComponent component in Components.Values)
                component.Init();
            OnInit?.Invoke(this);
        }

        public static event Action<CelesteNetClientContext> OnStart;

        public void Start() {
            if (Client == null)
                return;

            Client.Start();
            foreach (CelesteNetGameComponent component in Components.Values)
                component.Start();

            OnStart?.Invoke(this);

            Started = true;
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            lock (MainThreadQueue)
                while (MainThreadQueue.Count > 0)
                    MainThreadQueue.Dequeue()();

            if (Started && !(Client?.IsAlive ?? true))
                Dispose();
        }

        internal void _RunOnMainThread(Action action, bool wait = false)
            => RunOnMainThread(action, wait);
        protected void RunOnMainThread(Action action, bool wait = false) {
            if (Thread.CurrentThread == MainThreadHelper.MainThread) {
                action();
                return;
            }

            using (ManualResetEvent waiter = wait ? new ManualResetEvent(false) : null) {
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
                    MainThreadQueue.Enqueue(action);

                if (wait)
                    WaitHandle.WaitAny(new WaitHandle[] { waiter });
            }
        }

        public static event Action<CelesteNetClientContext> OnDispose;

        protected override void Dispose(bool disposing) {
            IsDisposed = true;

            bool reconnect = false;
            if (CelesteNetClientModule.Instance.Context == this) {
                reconnect = CelesteNetClientModule.Settings.AutoReconnect && CelesteNetClientModule.Settings.WantsToBeConnected;
                CelesteNetClientModule.Instance.Context = null;
                CelesteNetClientModule.Settings.Connected = false;
            }

            base.Dispose(disposing);

            Client?.Dispose();
            Client = null;

            Celeste.Instance.Components.Remove(this);

            foreach (CelesteNetGameComponent component in Components.Values)
                if (component.AutoDispose)
                    component.Dispose();

            OnDispose?.Invoke(this);

            if (Status != null) {
                if (Status.Spin) {
                    Status.Set("Disconnected", 3f, false);
                    if (reconnect) {
                        QueuedTaskHelper.Do("CelesteNetAutoReconnect", 1D, () => CelesteNetClientModule.Settings.Connected = true);
                    }
                }
                Status.AutoDispose = true;
            }
        }

    }
}
