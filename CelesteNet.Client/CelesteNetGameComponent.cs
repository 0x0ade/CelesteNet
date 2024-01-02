using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using System;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client {
    public abstract class CelesteNetGameComponent : DrawableGameComponent, Components.ITickReceiver {

        public const int UI_WIDTH = 1920;
        public const int UI_HEIGHT = 1080;

        public static bool IsDrawingUI;

        public CelesteNetClientContext Context;
        public CelesteNetClient Client => Context?.Client;
        public CelesteNetClientSettings ClientSettings => Context?.Client?.Settings ?? CelesteNetClientModule.Settings;
        public CelesteNetClientSettings Settings => CelesteNetClientModule.Settings;

        public bool Persistent = false, AutoDispose = true;

        private bool IsDisposed;

        public CelesteNetGameComponent(CelesteNetClientContext context, Game game)
            : base(game) {

            Context = context;

            UpdateOrder = 10000;
            DrawOrder = 10000;

            Enabled = false;
        }

        public virtual void Init() {
            Enabled = true;
            Client?.Data?.RegisterHandlersIn(this);
            if (Client?.Data == null) {
                Logger.Log(LogLevel.WRN, "gamecomp", $"CelesteNetGameComponent: Client?.Data is null during Init");
            }
        }

        public virtual void Start() {
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);
        }

        public virtual void Tick() {
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
            } else if (!IsDrawingUI && ((Context?.IsDisposed ?? true) || CelesteNetClientModule.Instance.UIRenderTarget == null)) {
                RenderContentWrap(gameTime, false);
            }
            if (CelesteNetClientModule.Instance.UIRenderTarget == null)
                Logger.Log(LogLevel.VVV, "gamecomp", $"CelesteNetGameComponent Draw UI RT is null");
        }

        public virtual void Disconnect(bool forceDispose = false) {
            if (Context == null && !forceDispose)
                throw new InvalidOperationException($"Component {this} not connected anymore!");
            Context = null;
            if ((AutoDispose || forceDispose) && !IsDisposed)
                Dispose();
        }

        public virtual void Reconnect(CelesteNetClientContext newCtx) {
            if (Context == null)
                throw new InvalidOperationException($"Component {this} not connected anymore!");
            if (!Persistent)
                throw new InvalidOperationException($"Component {this} not persistent!");
            Context = newCtx;
        }

        protected override void Dispose(bool disposing) {
            IsDisposed = true;
            Game.Components.Remove(this);
            base.Dispose(disposing);
            Client?.Data?.UnregisterHandlersIn(this);
        }

        protected void RunOnMainThread(Action action, bool wait = false)
            => Context._RunOnMainThread(action, wait);

    }
}
