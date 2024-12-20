using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components
{
    public unsafe class CelesteNetRenderHelperComponent : CelesteNetGameComponent {

        public int BlurScale => Settings.UIBlur switch {
            BlurQuality.LOW => 4,
            BlurQuality.MEDIUM => 4,
            BlurQuality.HIGH => 3,
            _ => 1,
        };
        public int BlurLowScale => Settings.UIBlur switch {
            BlurQuality.LOW => 8,
            BlurQuality.MEDIUM => 8,
            BlurQuality.HIGH => 6,
            _ => 1,
        };
        public int BlurWidth => UI_WIDTH / BlurScale;
        public int BlurHeight => UI_HEIGHT / BlurScale;
        public int BlurLowWidth => UI_WIDTH / BlurLowScale;
        public int BlurLowHeight => UI_HEIGHT / BlurLowScale;

        public RenderTarget2D? FakeRT;

        public RenderTarget2D? BlurXRT;
        public RenderTarget2D? BlurYRT;
        public RenderTarget2D? BlurLowRT;
        public RenderTarget2D? BlurRT;

        private bool _disconnected = false;

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

            Rectangle rect = new(xi, yi, wi, hi);

            if (BlurRT != null) {
                MDraw.SpriteBatch.Draw(
                    BlurRT,
                    rect, rect,
                    Color.White * Math.Min(1f, color.A / 255f * 2f)
                );
            } else {
                Logger.LogDetailed("cnet-rndrhlp", "BlurRT is null!");
            }

            MDraw.Rect(xi, yi, wi, hi, color);
        }

        public override void Initialize() {
            base.Initialize();

            Logger.Log(LogLevel.VVV, "cnet-rndrhlp", $"initializing Render Helper...");
            RunOnMainThread(() => {
                Logger.Log(LogLevel.VVV, "cnet-rndrhlp", $"Main thread initializing Render Helper");
                BlurRT = new(GraphicsDevice, UI_WIDTH, UI_HEIGHT, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

                IL.Celeste.Level.Render += ILRenderLevel;
                IL.Monocle.Engine.RenderCore += ILRenderCore;
            }, true);
        }

        public override void Disconnect(bool forceDispose = false) {
            try {
                Context?._RunOnMainThread(() => {
                    Logger.Log(LogLevel.VVV, "cnet-rndrhlp", $"Main thread disposing Render Helper");
                    IL.Celeste.Level.Render -= ILRenderLevel;
                    IL.Monocle.Engine.RenderCore -= ILRenderCore;

                    FakeRT?.Dispose();
                    BlurRT?.Dispose();
                    BlurRT = null;
                    BlurXRT?.Dispose();
                    BlurYRT?.Dispose();
                    BlurLowRT?.Dispose();
                    FakeRT = null;
                    _disconnected = true;
                }, true);
            } catch (ObjectDisposedException) {
                // It might already be too late to tell the main thread to do anything.
            }
            base.Disconnect(forceDispose);
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            Logger.Log(LogLevel.VVV, "cnet-rndrhlp", $"disposing Render Helper...");

            if (!_disconnected) {
                try {
                    MainThreadHelper.Schedule(() => {
                        Logger.Log(LogLevel.VVV, "cnet-rndrhlp", $"Main thread disposing Render Helper");
                        IL.Celeste.Level.Render -= ILRenderLevel;
                        IL.Monocle.Engine.RenderCore -= ILRenderCore;

                        FakeRT?.Dispose();
                        BlurRT?.Dispose();
                        BlurRT = null;
                        BlurXRT?.Dispose();
                        BlurYRT?.Dispose();
                        BlurLowRT?.Dispose();
                        FakeRT = null;
                    });
                } catch (ObjectDisposedException) {
                    // It might already be too late to tell the main thread to do anything.
                }
            }
        }

        private RenderTarget2D? GetFakeRT(RenderTarget2D realRT) {
            if (realRT != null)
                return realRT;

            if (Engine.Instance != null && Engine.Scene is AssetReloadHelper)
                return null;

            if (BlurRT == null)
                return null;

            if (FakeRT != null && (FakeRT.Width != Engine.ViewWidth || FakeRT.Height != Engine.ViewHeight)) {
                FakeRT.Dispose();
                FakeRT = null;
            }

            if (FakeRT == null) {
                FakeRT = new(
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
            ILCursor c = new(il);

            VariableDefinition vd_tmpRealVP = new(il.Import(typeof(Viewport)));
            il.Body.Variables.Add(vd_tmpRealVP);
            VariableDefinition vd_tmpRealRT = new(il.Import(typeof(RenderTarget2D)));
            il.Body.Variables.Add(vd_tmpRealRT);

            c.EmitDelegate<Func<Viewport>>(() => {
                Viewport tmpRealVP = Engine.Viewport;
                GraphicsDevice.Viewport = new(0, 0, Engine.ViewWidth, Engine.ViewHeight);
                return tmpRealVP;
            });
            c.Emit(OpCodes.Stloc, vd_tmpRealVP);

            c.GotoNext(i => i.MatchCallOrCallvirt(typeof(GraphicsDevice), "SetRenderTarget"));
            c.Emit(OpCodes.Dup);
            c.Emit(OpCodes.Stloc, vd_tmpRealRT);
            c.EmitDelegate<Func<RenderTarget2D, RenderTarget2D?>>(GetFakeRT);

            c.GotoNext(MoveType.After, i => i.MatchCallOrCallvirt(typeof(GraphicsDevice), "Clear"));
            c.Emit(OpCodes.Ldloc, vd_tmpRealVP);
            c.EmitDelegate<Action<Viewport>>((tmpRealVP) => {
                Engine.SetViewport(tmpRealVP);
                GraphicsDevice.Viewport = new(0, 0, Engine.ViewWidth, Engine.ViewHeight);
            });

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
                    Vector2 blurScale = new(BlurWidth / (float) tmpRealVP.Width, BlurHeight / (float) tmpRealVP.Height);
                    Vector2 blurScaleLow = new(BlurLowWidth / (float) BlurWidth, BlurLowHeight / (float) BlurHeight);
                    Vector2 blurScaleInv = new(UI_WIDTH / (float) BlurLowWidth, UI_HEIGHT / (float) BlurLowHeight);

                    switch (Settings.UIBlur) {
                        case BlurQuality.OFF:
                        default:
                            blurrad = 0;
                            blurdist = 0;
                            blurScaleInv = new(UI_WIDTH / (float) tmpRealVP.Width, UI_HEIGHT / (float) tmpRealVP.Height);
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
                            BlurXRT = new(GraphicsDevice, BlurWidth, BlurHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                        }
                        if (BlurYRT == null || BlurYRT.Width != BlurWidth || BlurYRT.Height != BlurHeight) {
                            BlurYRT?.Dispose();
                            BlurYRT = new(GraphicsDevice, BlurWidth, BlurHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                        }
                        if (BlurLowRT == null || BlurLowRT.Width != BlurLowWidth || BlurLowRT.Height != BlurLowHeight) {
                            BlurLowRT?.Dispose();
                            BlurLowRT = new(GraphicsDevice, BlurLowWidth, BlurLowHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
                        }

                        GraphicsDevice.SetRenderTarget(BlurXRT);
                        GraphicsDevice.Viewport = new(0, 0, BlurWidth, BlurHeight);
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

                        MDraw.SpriteBatch.Draw(FakeRT, new(0f, 0f), null, Color.White, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);

                        for (int x = blurrad - 1; x > 0; x--) {
                            float a = (blurrad - x) * 0.5f / blurrad;
                            Color c = new Color(a, a, a, a) * 0.5f;
                            MDraw.SpriteBatch.Draw(FakeRT, new(-x * blurdist - 0.5f, 0), null, c, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);
                            MDraw.SpriteBatch.Draw(FakeRT, new(+x * blurdist - 0.5f, 0), null, c, 0f, Vector2.Zero, blurScale, SpriteEffects.None, 0);
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

                        MDraw.SpriteBatch.Draw(BlurXRT, new(0f, 0f), null, Color.White, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);

                        for (int y = blurrad - 1; y > 0; y--) {
                            float a = (blurrad - y) * 0.5f / blurrad;
                            Color c = new Color(a, a, a, a) * 0.5f;
                            MDraw.SpriteBatch.Draw(BlurXRT, new(0f, -y * blurdist - 0.5f), null, c, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);
                            MDraw.SpriteBatch.Draw(BlurXRT, new(0f, +y * blurdist - 0.5f), null, c, 0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);
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

                        MDraw.SpriteBatch.Draw(BlurYRT, new(0f, 0f), null, Color.White, 0f, Vector2.Zero, blurScaleLow, SpriteEffects.None, 0);

                        MDraw.SpriteBatch.End();
                    }

                    GraphicsDevice.SetRenderTarget(BlurRT);
                    GraphicsDevice.Viewport = new(0, 0, UI_WIDTH, UI_HEIGHT);
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

                    List<CelesteNetGameComponent>? uiDC = Context?.DrawableComponents;
                    VirtualRenderTarget? uiRT = uiDC == null ? null : CelesteNetClientModule.Instance.UIRenderTarget;
                    if (uiRT != null) {
                        GraphicsDevice.SetRenderTarget(uiRT);
                        GraphicsDevice.Clear(Color.Transparent);
                        IsDrawingUI = true;
                        if (uiDC != null)
                            foreach (CelesteNetGameComponent component in uiDC)
                                component.Draw(null);
                        IsDrawingUI = false;
                    }

                    GraphicsDevice.SetRenderTarget(tmpRealRT);
                    GraphicsDevice.Viewport = tmpRealVP;
                    Engine.SetViewport(tmpRealVP);
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
            ILCursor c = new(il);

            VariableDefinition vd_tmpRealRT = new(il.Import(typeof(RenderTarget2D)));
            il.Body.Variables.Add(vd_tmpRealRT);

            while (c.TryGotoNext(i => i.MatchCallOrCallvirt(typeof(GraphicsDevice), "SetRenderTarget"))) {
                c.Emit(OpCodes.Dup);
                c.Emit(OpCodes.Stloc, vd_tmpRealRT);
                c.EmitDelegate<Func<RenderTarget2D, RenderTarget2D?>>(GetFakeRT);

                int tmpCIndex = c.Index;

                if (c.TryGotoNext(MoveType.After, i => i.MatchCallOrCallvirt(typeof(GraphicsDevice), "set_Viewport"))) {
                    c.Emit(OpCodes.Ldloc, vd_tmpRealRT);
                    c.EmitDelegate<Action<RenderTarget2D>>((tmpRealRT) => {
                        if (tmpRealRT == null) {
                            GraphicsDevice.Viewport = new(0, 0, Engine.ViewWidth, Engine.ViewHeight);
                        }
                    });
                }

                c.Index = tmpCIndex + 1;
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
