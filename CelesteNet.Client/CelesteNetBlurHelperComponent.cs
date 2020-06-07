using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetBlurHelperComponent : CelesteNetGameComponent {

        public const int BLUR_SCALE = 3;
        public const int BLUR_WIDTH = UI_WIDTH / BLUR_SCALE;
        public const int BLUR_HEIGHT = UI_HEIGHT / BLUR_SCALE;

        public RenderTarget2D RTGame;
        public bool ForceRTGameAsBackbuffer;

        public RenderTarget2D RTBlurX;
        public RenderTarget2D RTBlur;

        private Hook h_SetRenderTarget;

        public CelesteNetBlurHelperComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;
        }

        public void Rect(float x, float y, float width, float height, Color color) {
            int xi = (int) Math.Floor(x);
            int yi = (int) Math.Floor(y);
            int wi = (int) Math.Ceiling(x + width) - xi;
            int hi = (int) Math.Ceiling(y + height) - yi;
            
            MDraw.SpriteBatch.Draw(
                RTBlur,
                new Rectangle(xi, yi, wi, hi),
                new Rectangle(xi / BLUR_SCALE, yi / BLUR_SCALE, wi / BLUR_SCALE, hi / BLUR_SCALE),
                Color.White * Math.Min(1f, color.A / 255f * 2f)
            );
            
            MDraw.Rect(xi, yi, wi, hi, color);
        }

        public override void Init() {
            base.Init();

            On.Celeste.Celeste.RenderCore += OnRenderCore;
            h_SetRenderTarget = new Hook(
                typeof(GraphicsDevice).GetMethod("SetRenderTarget", new Type[] { typeof(RenderTarget2D) }),
                new Action<Action<GraphicsDevice, RenderTarget2D>, GraphicsDevice, RenderTarget2D>(OnSetRenderTarget)
            );
        }

        public override void Initialize() {
            base.Initialize();

            RTBlurX = new RenderTarget2D(GraphicsDevice, BLUR_WIDTH, BLUR_HEIGHT, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            RTBlur = new RenderTarget2D(GraphicsDevice, BLUR_WIDTH, BLUR_HEIGHT, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            try {
                On.Celeste.Celeste.RenderCore -= OnRenderCore;
                h_SetRenderTarget?.Dispose();
            } catch (InvalidOperationException) {
            }

            MainThreadHelper.Do(() => {
                RTGame?.Dispose();
                RTBlurX?.Dispose();
                RTBlur?.Dispose();
                RTGame = null;
            });
        }

        private void OnRenderCore(On.Celeste.Celeste.orig_RenderCore orig, Celeste self) {
            ForceRTGameAsBackbuffer = true;
            Viewport viewportPrev = Engine.Viewport;
            Engine.SetViewport(new Viewport(0, 0, Engine.ViewWidth, Engine.ViewHeight));

            orig(self);

            ForceRTGameAsBackbuffer = false;
            Engine.SetViewport(viewportPrev);

            if (RTGame != null) {
                const int blurrad = 16;
                const float blurdist = 0.6f;
                Vector2 blurScale = new Vector2(BLUR_WIDTH / (float) viewportPrev.Width, BLUR_HEIGHT / (float) viewportPrev.Height);

                GraphicsDevice.Viewport = new Viewport(0, 0, BLUR_WIDTH, BLUR_HEIGHT);

                GraphicsDevice.SetRenderTarget(RTBlurX);
                GraphicsDevice.Clear(Engine.ClearColor);
                MDraw.SpriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.AnisotropicClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    Matrix.Identity
                );

                MDraw.SpriteBatch.Draw(RTGame, new Vector2(0f, 0f), null, Color.White, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);

                for (int x = blurrad - 1; x > 0; x--) {
                    float a = (blurrad - x) * 0.5f / blurrad;
                    Color c = new Color(a, a, a, a) * 0.5f;
                    MDraw.SpriteBatch.Draw(RTGame, new Vector2(-x * blurdist - 0.5f, 0), null, c, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);
                    MDraw.SpriteBatch.Draw(RTGame, new Vector2(+x * blurdist - 0.5f, 0), null, c, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);
                }

                MDraw.SpriteBatch.End();

                GraphicsDevice.SetRenderTarget(RTBlur);
                GraphicsDevice.Clear(Engine.ClearColor);
                MDraw.SpriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.AnisotropicClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    Matrix.Identity
                );

                MDraw.SpriteBatch.Draw(RTBlurX, new Vector2(0f, 0f), null, Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);

                for (int y = blurrad - 1; y > 0; y--) {
                    float a = (blurrad - y) * 0.5f / blurrad;
                    Color c = new Color(a, a, a, a) * 0.5f;
                    MDraw.SpriteBatch.Draw(RTBlurX, new Vector2(0f, -y * blurdist - 0.5f), null, c, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);
                    MDraw.SpriteBatch.Draw(RTBlurX, new Vector2(0f, +y * blurdist - 0.5f), null, c, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);
                }

                MDraw.SpriteBatch.End();

            }

            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Viewport = Engine.Viewport;
            GraphicsDevice.Clear(Engine.ClearColor);

            if (RTGame != null) {
                MDraw.SpriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.Opaque,
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    Matrix.Identity
                );

                MDraw.SpriteBatch.Draw(RTGame, Vector2.Zero, Color.White);

                MDraw.SpriteBatch.End();
            }
        }

        private void OnSetRenderTarget(Action<GraphicsDevice, RenderTarget2D> orig, GraphicsDevice self, RenderTarget2D renderTarget) {
            if (ForceRTGameAsBackbuffer && renderTarget == null) {
                if (RTGame != null && (RTGame.Width != Engine.ViewWidth || RTGame.Height != Engine.ViewHeight)) {
                    RTGame.Dispose();
                    RTGame = null;
                }

                if (RTGame == null) {
                    RTGame = new RenderTarget2D(
                        self,
                        Engine.ViewWidth,
                        Engine.ViewHeight,
                        false,
                        SurfaceFormat.Color,
                        self.PresentationParameters.DepthStencilFormat,
                        0,
                        RenderTargetUsage.DiscardContents
                    );
                }

                renderTarget = RTGame;
            }

            orig(self, renderTarget);
        }

    }
}
