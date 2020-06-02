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
    public abstract class CelesteNetGameComponent : DrawableGameComponent {

        public const float UIW = 1920f;
        public const float UIH = 1080f;

        public CelesteNetClientComponent Context;

        public bool AutoRemove = true;

        public CelesteNetGameComponent(CelesteNetClientComponent context, Game game)
            : base(game) {

            Context = context;

            UpdateOrder = 10000;
            DrawOrder = 10000;
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
            base.Draw(gameTime);

            // TODO: Figure out why rendering to a buffer doesn't work.
            if (HiresRenderer.Buffer == null || true) {
                RenderContentWrap(gameTime, false);
                return;
            }

            Engine.Graphics.GraphicsDevice.SetRenderTarget(HiresRenderer.Buffer);
            Engine.Graphics.GraphicsDevice.Clear(Color.Transparent);
            RenderContentWrap(gameTime, true);

            Engine.Graphics.GraphicsDevice.SetRenderTarget(null);
            MDraw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.NonPremultiplied,
                SamplerState.LinearClamp,
                DepthStencilState.Default,
                RasterizerState.CullNone,
                null,
                Engine.ScreenMatrix
            );
            MDraw.SpriteBatch.Draw(HiresRenderer.Buffer, new Vector2(-1f, -1f), Color.White);
            MDraw.SpriteBatch.End();
        }

        protected override void Dispose(bool disposing) {
            if (AutoRemove)
                Game.Components.Remove(this);
            base.Dispose(disposing);
        }

    }
}
