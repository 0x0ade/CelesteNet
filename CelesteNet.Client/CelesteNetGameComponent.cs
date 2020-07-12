using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client {
    public abstract class CelesteNetGameComponent : DrawableGameComponent {

        public const int UI_WIDTH = 1920;
        public const int UI_HEIGHT = 1080;

        public static bool IsDrawingUI;

        public CelesteNetClientContext Context;
        public CelesteNetClient Client => Context?.Client;
        public CelesteNetClientSettings ClientSettings => Context?.Client?.Settings ?? CelesteNetClientModule.Settings;
        public CelesteNetClientSettings Settings => CelesteNetClientModule.Settings;

        public bool AutoRemove = true;
        public bool AutoDispose = true;

        public CelesteNetGameComponent(CelesteNetClientContext context, Game game)
            : base(game) {

            Context = context;

            UpdateOrder = 10000;
            DrawOrder = 10000;

            Enabled = false;
        }

        public virtual void Init() {
            Enabled = true;
            Client.Data.RegisterHandlersIn(this);
        }

        public virtual void Start() {
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (AutoRemove && Context.Game == null)
                Dispose();
        }

        protected virtual void Render(GameTime gameTime, bool toBuffer) {
        }

        protected virtual void RenderContentWrap(GameTime gameTime, bool toBuffer) {
            MDraw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                toBuffer ? Matrix.Identity : Engine.ScreenMatrix
            );

            Render(gameTime, toBuffer);

            MDraw.SpriteBatch.End();
        }

        public override void Draw(GameTime gameTime) {
            if (IsDrawingUI) {
                RenderContentWrap(gameTime, true);
            } else if (!IsDrawingUI && CelesteNetClientModule.Instance.UIRenderTarget == null) {
                RenderContentWrap(gameTime, false);
            }
        }

        protected override void Dispose(bool disposing) {
            if (AutoRemove)
                Game.Components.Remove(this);
            base.Dispose(disposing);
            Client?.Data.UnregisterHandlersIn(this);
        }

        protected void RunOnMainThread(Action action, bool wait = false)
            => Context._RunOnMainThread(action, wait);

    }
}
