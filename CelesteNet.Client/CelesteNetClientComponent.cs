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

        public CelesteNetClientComponent(Game game)
            : base(game) {

            UpdateOrder = -10000;

            Celeste.Instance.Components.Add(this);

            game.Components.Add(Status = new CelesteNetStatusComponent(this, game));
        }

        public void Init(CelesteNetClientSettings settings) {
            Client = new CelesteNetClient(settings);
        }

        public void Start() {
            Client?.Start();
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (!(Client?.IsAlive ?? true))
                Dispose();
        }

        protected override void Dispose(bool disposing) {
            if (CelesteNetClientModule.Instance.Context == this) {
                CelesteNetClientModule.Instance.Context = null;
                CelesteNetClientModule.Instance.Settings.Connected = false;
            }

            base.Dispose(disposing);

            Client?.Dispose();
            Client = null;

            Celeste.Instance.Components.Remove(this);

            Status.Set("Disconnected", 3f, false);
        }

    }
}
