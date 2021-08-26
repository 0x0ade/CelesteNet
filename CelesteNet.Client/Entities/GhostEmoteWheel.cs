using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    // TODO: This is taken mostly as is from GhostNet and can be improved.
    public class GhostEmoteWheel : Entity {

        public Entity Tracking;

        public float Alpha = 1f;

        protected float time = 0f;

        public bool Shown = false;
        protected bool popupShown = false;
        protected float popupTime = 100f;
        protected bool timeRateSet = false;

        public HashSet<string> TimeRateSkip = new();
        public bool ForceSetTimeRate;

        public float Angle = 0f;

        public int Selected = -1;
        protected int PrevSelected;
        protected float selectedTime = 0f;

        public MTexture BG = GFX.Gui["celestenet/iconwheel/bg"];
        public MTexture Line = GFX.Gui["celestenet/iconwheel/line"];
        public MTexture Indicator = GFX.Gui["celestenet/iconwheel/indicator"];

        public Color TextSelectColorA = Calc.HexToColor("84FF54");
        public Color TextSelectColorB = Calc.HexToColor("FCFF59");

        public GhostEmoteWheel(Entity tracking)
            : base(Vector2.Zero) {
            Tracking = tracking;

            Tag = TagsExt.SubHUD;
            Depth = -1;
        }

        public override void Update() {
            // Update only runs while the level is "alive" (scene not paused or frozen).

            if (TimeRateSkip.Count == 0 || ForceSetTimeRate) {
                if (Shown && !timeRateSet) {
                    Engine.TimeRate = 0.25f;
                    timeRateSet = true;

                } else if (!Shown && timeRateSet) {
                    Engine.TimeRate = 1f;
                    timeRateSet = false;
                }
            }

            base.Update();

            if (Tracking == null || Tracking?.Scene != Scene)
                RemoveSelf();
        }

        public override void Render() {
            base.Render();

            string[] emotes = CelesteNetClientModule.Settings.Emotes;

            // Update can halt in the pause menu.

            if (Shown) {
                Angle = CelesteNetClientModule.Instance.JoystickEmoteWheel.Value.Angle();
                float angle = (float) ((Angle + Math.PI * 2f) % (Math.PI * 2f));
                float start = (-0.5f / emotes.Length) * 2f * (float) Math.PI;
                if (2f * (float) Math.PI + start < angle) {
                    // Angle should be start < angle < 0, but is (TAU + start) < angle < TAU
                    angle -= 2f * (float) Math.PI;
                }
                for (int i = 0; i < emotes.Length; i++) {
                    float min = ((i - 0.5f) / emotes.Length) * 2f * (float) Math.PI;
                    float max = ((i + 0.5f) / emotes.Length) * 2f * (float) Math.PI;
                    if (min <= angle && angle <= max) {
                        Selected = i;
                        break;
                    }
                }
            }

            time += Engine.RawDeltaTime;

            if (!Shown) {
                Selected = -1;
            }
            selectedTime += Engine.RawDeltaTime;
            if (PrevSelected != Selected) {
                selectedTime = 0f;
                PrevSelected = Selected;
            }

            float popupAlpha;
            float popupScale;

            popupTime += Engine.RawDeltaTime;
            if (Shown && !popupShown) {
                popupTime = 0f;
            } else if ((Shown && popupTime > 1f) ||
                (!Shown && popupTime < 1f)) {
                popupTime = 1f;
            }
            popupShown = Shown;

            if (popupTime < 0.1f) {
                float t = popupTime / 0.1f;
                // Pop in.
                popupAlpha = Ease.CubeOut(t);
                popupScale = Ease.ElasticOut(t);

            } else if (popupTime < 1f) {
                // Stay.
                popupAlpha = 1f;
                popupScale = 1f;

            } else {
                float t = (popupTime - 1f) / 0.2f;
                // Fade out.
                popupAlpha = 1f - Ease.CubeIn(t);
                popupScale = 1f - 0.2f * Ease.CubeIn(t);
            }

            float alpha = Alpha * popupAlpha;

            if (alpha <= 0f)
                return;

            if (Tracking == null)
                return;

            Level level = SceneAs<Level>();
            if (level == null)
                return;

            popupScale *= level.GetScreenScale();

            Vector2 pos = Tracking.Position;
            pos.Y -= 8f;

            pos = level.WorldToScreen(pos);

            float radius = BG.Width * 0.5f * 0.75f * popupScale;

            pos = pos.Clamp(
                0f + radius, 0f + radius,
                1920f - radius, 1080f - radius
            );

            // Draw.Circle(pos, radius, Color.Black * 0.8f * alpha * alpha, radius * 0.6f * (1f + 0.2f * (float) Math.Sin(time)), 8);
            BG.DrawCentered(
                pos,
                Color.White * alpha * alpha * alpha,
                Vector2.One * popupScale
            );

            Indicator.DrawCentered(
                pos,
                Color.White * alpha * alpha * alpha,
                Vector2.One * popupScale,
                Angle
            );

            float selectedScale = 1.2f - 0.2f * Calc.Clamp(Ease.CubeOut(selectedTime / 0.1f), 0f, 1f) + (float) Math.Sin(time * 1.8f) * 0.05f;

            for (int i = 0; i < emotes.Length; i++) {
                Line.DrawCentered(
                    pos,
                    Color.White * alpha * alpha * alpha,
                    Vector2.One * popupScale,
                    ((i + 0.5f) / emotes.Length) * 2f * (float) Math.PI
                );

                string emote = emotes[i];
                if (string.IsNullOrEmpty(emote))
                    continue;

                float a = (i / (float) emotes.Length) * 2f * (float) Math.PI;
                Vector2 emotePos = pos + new Vector2(
                    (float) Math.Cos(a),
                    (float) Math.Sin(a)
                ) * radius;

                if (GhostEmote.IsIcon(emote)) {
                    MTexture icon = GhostEmote.GetIcon(emote, Selected == i ? selectedTime : 0f);
                    if (icon == null)
                        continue;

                    Vector2 iconSize = new(icon.Width, icon.Height);
                    float iconScale = (GhostEmote.Size / Math.Max(iconSize.X, iconSize.Y)) * 0.24f * popupScale;

                    icon.DrawCentered(
                        emotePos,
                        Color.White * (Selected == i ? (Calc.BetweenInterval(selectedTime, 0.1f) ? 0.9f : 1f) : 0.7f) * alpha,
                        Vector2.One * (Selected == i ? selectedScale : 1f) * iconScale
                    );

                } else {
                    Vector2 textSize = CelesteNetClientFont.Measure(emote);
                    float textScale = (GhostEmote.Size / Math.Max(textSize.X, textSize.Y)) * 0.24f * popupScale;

                    CelesteNetClientFont.DrawOutline(
                        emote,
                        emotePos,
                        new(0.5f, 0.5f),
                        Vector2.One * (Selected == i ? selectedScale : 1f) * textScale,
                        (Selected == i ? (Calc.BetweenInterval(selectedTime, 0.1f) ? TextSelectColorA : TextSelectColorB) : Color.LightSlateGray) * alpha,
                        2f,
                        Color.Black * alpha * alpha * alpha
                    );
                }
            }
        }

    }
}
