using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public unsafe class CelesteNetBlurHelperComponent : CelesteNetGameComponent {

        public int BlurScale {
            get {
                switch (Settings.UIBlur) {
                    case BlurQuality.Off:
                    default:
                        return 1;

                    case BlurQuality.LQ:
                        return 3;

                    case BlurQuality.HQ:
                        return 3;
                }
            }
        }
        public int BlurWidth => UI_WIDTH / BlurScale;
        public int BlurHeight => UI_HEIGHT / BlurScale;

        public RenderTarget2D FakeRT;

        public RenderTarget2D BlurXRT;
        public RenderTarget2D BlurYRT;
        public RenderTarget2D BlurRT;

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

            Rectangle rect = new Rectangle(xi, yi, wi, hi);
            
            MDraw.SpriteBatch.Draw(
                BlurRT,
                rect, rect,
                Color.White * Math.Min(1f, color.A / 255f * 2f)
            );
            
            MDraw.Rect(xi, yi, wi, hi, color);
        }

        public override void Initialize() {
            base.Initialize();

            BlurRT = new RenderTarget2D(GraphicsDevice, UI_WIDTH, UI_HEIGHT, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

            IL.Celeste.Level.Render += ILRenderLevel;
            IL.Monocle.Engine.RenderCore += ILRenderCore;
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            MainThreadHelper.Do(() => {
                IL.Celeste.Level.Render -= ILRenderLevel;
                IL.Monocle.Engine.RenderCore -= ILRenderCore;

                FakeRT?.Dispose();
                BlurXRT?.Dispose();
                BlurYRT?.Dispose();
                FakeRT = null;
            });
        }

        private RenderTarget2D GetFakeRT(RenderTarget2D realRT) {
            if (realRT != null)
                return realRT;

            if (Engine.Instance != null && Engine.Scene is AssetReloadHelper)
                return null;

            if (FakeRT != null && (FakeRT.Width != Engine.ViewWidth || FakeRT.Height != Engine.ViewHeight)) {
                FakeRT.Dispose();
                FakeRT = null;
            }

            if (FakeRT == null) {
                FakeRT = new RenderTarget2D(
                    GraphicsDevice,
                    Engine.ViewWidth,
                    Engine.ViewHeight,
                    false,
                    SurfaceFormat.Color,
                    GraphicsDevice.PresentationParameters.DepthStencilFormat,
                    0,
                    RenderTargetUsage.DiscardContents
                );
            }

            return FakeRT;
        }

        private void ILRenderCore(ILContext il) {
            ILCursor c = new ILCursor(il);

            VariableDefinition vd_tmpRealVP = new VariableDefinition(il.Import(typeof(Viewport)));
            il.Body.Variables.Add(vd_tmpRealVP);
            VariableDefinition vd_tmpRealRT = new VariableDefinition(il.Import(typeof(RenderTarget2D)));
            il.Body.Variables.Add(vd_tmpRealRT);

            c.EmitDelegate<Func<Viewport>>(() => {
                Viewport tmpRealVP = Engine.Viewport;
                Engine.SetViewport(new Viewport(0, 0, Engine.ViewWidth, Engine.ViewHeight));
                return tmpRealVP;
            });
            c.Emit(OpCodes.Stloc, vd_tmpRealVP);

            c.GotoNext(i => i.MatchCallOrCallvirt(typeof(GraphicsDevice), "SetRenderTarget"));
            c.Emit(OpCodes.Dup);
            c.Emit(OpCodes.Stloc, vd_tmpRealRT);
            c.EmitDelegate<Func<RenderTarget2D, RenderTarget2D>>(GetFakeRT);

            c.GotoNext(i => i.MatchRet());
            c.Emit(OpCodes.Ldloc, vd_tmpRealVP);
            c.Emit(OpCodes.Ldloc, vd_tmpRealRT);
            c.EmitDelegate<Action<Viewport, RenderTarget2D>>((tmpRealVP, tmpRealRT) => {
                Engine.SetViewport(tmpRealVP);

                if (Engine.Instance != null && Engine.Scene is AssetReloadHelper)
                    return;

                if (FakeRT != null) {
                    int blurrad;
                    float blurdist;
                    Vector2 blurScale = new Vector2(BlurWidth / (float) tmpRealVP.Width, BlurHeight / (float) tmpRealVP.Height);
                    Vector2 blurScaleInv = new Vector2(UI_WIDTH / (float) BlurWidth, UI_HEIGHT / (float) BlurHeight);

                    switch (Settings.UIBlur) {
                        case BlurQuality.Off:
                        default:
                            blurrad = 0;
                            blurdist = 0;
                            blurScaleInv = new Vector2(UI_WIDTH / (float) tmpRealVP.Width, UI_HEIGHT / (float) tmpRealVP.Height);
                            break;

                        case BlurQuality.LQ:
                            blurrad = 8;
                            blurdist = 0.8f;
                            break;

                        case BlurQuality.HQ:
                            blurrad = 16;
                            blurdist = 0.6f;
                            break;
                    }

                    if (blurrad > 0) {
                        if (BlurXRT == null || BlurXRT.Width != BlurWidth || BlurXRT.Height != BlurHeight) {
                            BlurXRT?.Dispose();
                            BlurXRT = new RenderTarget2D(GraphicsDevice, BlurWidth, BlurHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                        }
                        if (BlurYRT == null || BlurYRT.Width != BlurWidth || BlurYRT.Height != BlurHeight) {
                            BlurYRT?.Dispose();
                            BlurYRT = new RenderTarget2D(GraphicsDevice, BlurWidth, BlurHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                        }

                        GraphicsDevice.Viewport = new Viewport(0, 0, BlurWidth, BlurHeight);

                        GraphicsDevice.SetRenderTarget(BlurXRT);
                        GraphicsDevice.Clear(Engine.ClearColor);
                        MDraw.SpriteBatch.Begin(
                            SpriteSortMode.Deferred,
                            BlendState.AlphaBlend,
                            SamplerState.LinearClamp,
                            DepthStencilState.None,
                            RasterizerState.CullNone,
                            null,
                            Matrix.Identity
                        );

                        MDraw.SpriteBatch.Draw(FakeRT, new Vector2(0f, 0f), null, Color.White, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);

                        for (int x = blurrad - 1; x > 0; x--) {
                            float a = (blurrad - x) * 0.5f / blurrad;
                            Color c = new Color(a, a, a, a) * 0.5f;
                            MDraw.SpriteBatch.Draw(FakeRT, new Vector2(-x * blurdist - 0.5f, 0), null, c, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);
                            MDraw.SpriteBatch.Draw(FakeRT, new Vector2(+x * blurdist - 0.5f, 0), null, c, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);
                        }

                        MDraw.SpriteBatch.End();

                        GraphicsDevice.SetRenderTarget(BlurYRT);
                        GraphicsDevice.Clear(Engine.ClearColor);
                        MDraw.SpriteBatch.Begin(
                            SpriteSortMode.Deferred,
                            BlendState.AlphaBlend,
                            SamplerState.LinearClamp,
                            DepthStencilState.None,
                            RasterizerState.CullNone,
                            null,
                            Matrix.Identity
                        );

                        MDraw.SpriteBatch.Draw(BlurXRT, new Vector2(0f, 0f), null, Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);

                        for (int y = blurrad - 1; y > 0; y--) {
                            float a = (blurrad - y) * 0.5f / blurrad;
                            Color c = new Color(a, a, a, a) * 0.5f;
                            MDraw.SpriteBatch.Draw(BlurXRT, new Vector2(0f, -y * blurdist - 0.5f), null, c, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);
                            MDraw.SpriteBatch.Draw(BlurXRT, new Vector2(0f, +y * blurdist - 0.5f), null, c, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);
                        }

                        MDraw.SpriteBatch.End();
                    }

                    GraphicsDevice.SetRenderTarget(BlurRT);
                    GraphicsDevice.Clear(Engine.ClearColor);
                    MDraw.SpriteBatch.Begin(
                        SpriteSortMode.Deferred,
                        BlendState.Opaque,
                        SamplerState.LinearClamp,
                        DepthStencilState.None,
                        RasterizerState.CullNone,
                        null,
                        Matrix.Identity
                    );

                    MDraw.SpriteBatch.Draw(blurrad > 0 ? BlurYRT : FakeRT, new Vector2(0f, 0f), null, Color.White, 0f, Vector2.Zero, blurScaleInv, SpriteEffects.None, 0);

                    MDraw.SpriteBatch.End();

                    GraphicsDevice.SetRenderTarget(tmpRealRT);
                    GraphicsDevice.Viewport = Engine.Viewport;
                    GraphicsDevice.Clear(Engine.ClearColor);

                    MDraw.SpriteBatch.Begin(
                        SpriteSortMode.Deferred,
                        BlendState.Opaque,
                        SamplerState.LinearClamp,
                        DepthStencilState.None,
                        RasterizerState.CullNone,
                        null,
                        Matrix.Identity
                    );

                    MDraw.SpriteBatch.Draw(FakeRT, Vector2.Zero, Color.White);

                    MDraw.SpriteBatch.End();
                }
            });
        }

        private void ILRenderLevel(ILContext il) {
            ILCursor c = new ILCursor(il);

            while (c.TryGotoNext(i => i.MatchCallOrCallvirt(typeof(GraphicsDevice), "SetRenderTarget"))) {
                c.EmitDelegate<Func<RenderTarget2D, RenderTarget2D>>(GetFakeRT);
                c.Index++;
            }
        }

        public enum BlurQuality {
            Off,
            LQ,
            HQ
        }

    }
}
