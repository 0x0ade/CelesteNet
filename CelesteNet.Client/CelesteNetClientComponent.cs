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
    public class CelesteNetClientComponent : DrawableGameComponent {

        const float UIW = 1920f;
        const float UIH = 1080f;

        private bool cogShow;
        private MTexture cogTexture;
        private string cogText;
        private float cogTime;
        private float cogTimeTotal;
        private const float cogTimeIn = 0.3f;
        private const float cogTimeOut = 0.15f;

        public CelesteNetClient Client;

        public CelesteNetClientComponent(Game game)
            : base(game) {

            UpdateOrder = -10000;
            DrawOrder = 10000;
        }

        protected override void LoadContent() {
            base.LoadContent();

            cogTexture = GFX.Gui["reloader/cogwheel"];
        }

        public void Init(CelesteNetClientSettings settings) {
            Client = new CelesteNetClient(settings);
        }

        public void Start() {
            Client.Start();
        }

        public void SetStatus(string text) {
            if (string.IsNullOrEmpty(text)) {
                cogShow = false;
                return;
            }

            cogText = text;
            cogShow = true;
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (cogShow || cogTime != 0f) {
                if (!cogShow && cogTime >= cogTimeOut) {
                    cogTime = 0f;
                    cogTimeTotal = 0f;
                } else if (cogTime < cogTimeIn) {
                    cogTime += Engine.RawDeltaTime;
                } else {
                    cogTime = cogTimeIn;
                }
            }
            cogTimeTotal += Engine.RawDeltaTime;


            if (!(Client?.IsAlive ?? true)) {
                Dispose();
            }
        }

        private void RenderContent(bool toBuffer) {
            MDraw.SpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend,
                SamplerState.LinearClamp,
                DepthStencilState.None,
                RasterizerState.CullNone,
                null,
                toBuffer ? Matrix.Identity : Engine.ScreenMatrix
            );

            if (cogShow || cogTime != 0f)
                RenderCog();

            MDraw.SpriteBatch.End();
        }

        private void RenderCog() {
            float a = Ease.SineInOut(cogShow ? (cogTime / cogTimeIn) : (1f - cogTime / cogTimeOut));

            Vector2 anchor = new Vector2(96f, UIH - 96f);

            Vector2 pos = anchor + new Vector2(0f, 0f);
            float cogScale = MathHelper.Lerp(0.2f, 0.25f, Ease.CubeOut(a));
            if (!(cogTexture?.Texture?.Texture?.IsDisposed ?? true)) {
                float cogRot = cogTimeTotal * 4f;
                for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                        if (x != 0 || y != 0)
                            cogTexture.DrawCentered(pos + new Vector2(x, y), Color.Black * a * a * a * a, cogScale, cogRot);
                cogTexture.DrawCentered(pos, Color.White * a, cogScale, cogRot);
            }

            pos = anchor + new Vector2(48f, 0f);
            string text = cogText;
            try {
                if (!string.IsNullOrEmpty(text) && Dialog.Language != null && ActiveFont.Font != null) {
                    Vector2 size = ActiveFont.Measure(text);
                    ActiveFont.DrawOutline(text, pos + new Vector2(size.X * 0.5f, 0f), new Vector2(0.5f, 0.5f), Vector2.One * MathHelper.Lerp(0.8f, 1f, Ease.CubeOut(a)), Color.White * a, 2f, Color.Black * a * a * a * a);
                }
            } catch {
                // Whoops, we weren't ready to draw text yet...
            }
        }

        public override void Draw(GameTime gameTime) {
            base.Draw(gameTime);

            // TODO: Figure out why rendering to a buffer doesn't work.
            if (HiresRenderer.Buffer == null || true) {
                RenderContent(false);
                return;
            }

            Engine.Graphics.GraphicsDevice.SetRenderTarget(HiresRenderer.Buffer);
            Engine.Graphics.GraphicsDevice.Clear(Color.Transparent);
            RenderContent(true);

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
            if (CelesteNetClientModule.Instance.ClientComponent == this) {
                CelesteNetClientModule.Instance.ClientComponent = null;
                CelesteNetClientModule.Instance.Settings.Connected = false;
            }

            base.Dispose(disposing);

            Client?.Dispose();
            Client = null;

            Celeste.Instance.Components.Remove(this);
        }

    }
}
