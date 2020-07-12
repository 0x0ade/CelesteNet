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
    public unsafe class CelesteNetRenderHelperComponent : CelesteNetGameComponent {

        public int BlurScale {
            get {
                switch (Settings.UIBlur) {
                    case BlurQuality.OFF:
                    default:
                        return 1;

                    case BlurQuality.LOW:
                        return 4;

                    case BlurQuality.MEDIUM:
                        return 4;

                    case BlurQuality.HIGH:
                        return 3;
                }
            }
        }
        public int BlurLowScale {
            get {
                switch (Settings.UIBlur) {
                    case BlurQuality.OFF:
                    default:
                        return 1;

                    case BlurQuality.LOW:
                        return 8;

                    case BlurQuality.MEDIUM:
                        return 8;

                    case BlurQuality.HIGH:
                        return 6;
                }
            }
        }
        public int BlurWidth => UI_WIDTH / BlurScale;
        public int BlurHeight => UI_HEIGHT / BlurScale;
        public int BlurLowWidth => UI_WIDTH / BlurLowScale;
        public int BlurLowHeight => UI_HEIGHT / BlurLowScale;

        public RenderTarget2D FakeRT;

        public RenderTarget2D BlurXRT;
        public RenderTarget2D BlurYRT;
        public RenderTarget2D BlurLowRT;
        public RenderTarget2D BlurRT;

        public CelesteNetRenderHelperComponent(CelesteNetClientContext context, Game game)
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
                BlurRT?.Dispose();
                BlurXRT?.Dispose();
                BlurYRT?.Dispose();
                BlurLowRT?.Dispose();
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
                    Vector2 blurScaleLow = new Vector2(BlurLowWidth / (float) BlurWidth, BlurLowHeight / (float) BlurHeight);
                    Vector2 blurScaleInv = new Vector2(UI_WIDTH / (float) BlurLowWidth, UI_HEIGHT / (float) BlurLowHeight);

                    switch (Settings.UIBlur) {
                        case BlurQuality.OFF:
                        default:
                            blurrad = 0;
                            blurdist = 0;
                            blurScaleInv = new Vector2(UI_WIDTH / (float) tmpRealVP.Width, UI_HEIGHT / (float) tmpRealVP.Height);
                            break;

                        case BlurQuality.LOW:
                            blurrad = 3;
                            blurdist = 1.2f;
                            break;

                        case BlurQuality.MEDIUM:
                            blurrad = 5;
                            blurdist = 0.9f;
                            break;

                        case BlurQuality.HIGH:
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
                        if (BlurLowRT == null || BlurLowRT.Width != BlurLowWidth || BlurLowRT.Height != BlurLowHeight) {
                            BlurLowRT?.Dispose();
                            BlurLowRT = new RenderTarget2D(GraphicsDevice, BlurLowWidth, BlurLowHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                        }

                        GraphicsDevice.SetRenderTarget(BlurXRT);
                        GraphicsDevice.Viewport = new Viewport(0, 0, BlurWidth, BlurHeight);
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

                        GraphicsDevice.SetRenderTarget(BlurLowRT);
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

                        MDraw.SpriteBatch.Draw(BlurYRT, new Vector2(0f, 0f), null, Color.White, 0f, Vector2.Zero, blurScaleLow, SpriteEffects.None, 0);

                        MDraw.SpriteBatch.End();
                    }

                    GraphicsDevice.SetRenderTarget(BlurRT);
                    GraphicsDevice.Viewport = new Viewport(0, 0, UI_WIDTH, UI_HEIGHT);
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

                    MDraw.SpriteBatch.Draw(blurrad > 0 ? BlurLowRT : FakeRT, new Vector2(0f, 0f), null, Color.White, 0f, Vector2.Zero, blurScaleInv, SpriteEffects.None, 0);

                    MDraw.SpriteBatch.End();

                    VirtualRenderTarget uiRT = CelesteNetClientModule.Instance.UIRenderTarget;
                    if (uiRT != null) {
                        GraphicsDevice.SetRenderTarget(uiRT);
                        GraphicsDevice.Clear(Color.Transparent);
                        IsDrawingUI = true;
                        foreach (CelesteNetGameComponent component in Context.DrawableComponents)
                            component.Draw(null);
                        IsDrawingUI = false;
                    }

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

                    if (uiRT != null) {
                        MDraw.SpriteBatch.Begin(
                            SpriteSortMode.Deferred,
                            BlendState.AlphaBlend,
                            SamplerState.LinearClamp,
                            DepthStencilState.None,
                            RasterizerState.CullNone,
                            null,
                            Engine.ScreenMatrix
                        );

                        MDraw.SpriteBatch.Draw(uiRT, new Vector2(-1f, -1f), Color.White);

                        MDraw.SpriteBatch.End();
                    }
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
            OFF,
            LOW,
            MEDIUM,
            HIGH
        }

    }
}
