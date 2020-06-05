using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetClientComponent : GameComponent {

        public CelesteNetClient Client;

        public CelesteNetStatusComponent Status;
        public CelesteNetChatComponent Chat;

        public Dictionary<Type, CelesteNetGameComponent> Components = new Dictionary<Type, CelesteNetGameComponent>();

        public CelesteNetClientComponent(Game game)
            : base(game) {

            UpdateOrder = -10000;

            Celeste.Instance.Components.Add(this);

            Add(Status = new CelesteNetStatusComponent(this, game));
            Add(Chat = new CelesteNetChatComponent(this, game));

            foreach (Type type in FakeAssembly.GetFakeEntryAssembly().GetTypes()) {
                if (type.IsAbstract || !typeof(CelesteNetGameComponent).IsAssignableFrom(type) || Components.ContainsKey(type))
                    continue;

                Add((CelesteNetGameComponent) Activator.CreateInstance(type, this, game));
            }
        }

        protected void Add(CelesteNetGameComponent component) {
            Logger.Log(LogLevel.INF, "clientcomp", $"Added component: {component}");
            Components[component.GetType()] = component;
            Game.Components.Add(component);
        }

        public T Get<T>() where T : CelesteNetGameComponent
            => Components.TryGetValue(typeof(T), out CelesteNetGameComponent component) ? (T) component : null;

        public void Init(CelesteNetClientSettings settings) {
            Client = new CelesteNetClient(settings);
            foreach (CelesteNetGameComponent component in Components.Values)
                component.Init();
        }

        public void Start() {
            if (Client == null)
                return;

            Client.Start();
            foreach (CelesteNetGameComponent component in Components.Values)
                component.Start();
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (!(Client?.IsAlive ?? true))
                Dispose();
        }

        protected override void Dispose(bool disposing) {
            if (CelesteNetClientModule.Instance.Context == this) {
                CelesteNetClientModule.Instance.Context = null;
                CelesteNetClientModule.Settings.Connected = false;
            }

            base.Dispose(disposing);

            Client?.Dispose();
            Client = null;

            Celeste.Instance.Components.Remove(this);

            Status.Set("Disconnected", 3f, false);

            foreach (CelesteNetGameComponent component in Components.Values)
                if (component.AutoDispose)
                    component.Dispose();
        }

    }
}
