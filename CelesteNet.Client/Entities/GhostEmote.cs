using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    // TODO: This is taken mostly as is from GhostNet and can be improved.
    public class GhostEmote : Entity {

        public static readonly char IconPathsSeperator = ' ';

        public static float Size = 256f;

        public Entity Tracking;

        public string Value;

        public float Alpha = 1f;

        public bool PopIn = false;
        public bool FadeOut = false;
        public bool PopOut = false;
        public float AnimationTime;

        public bool Float = false;

        public float Time;

        public float PopupAlpha = 1f;
        public float PopupScale = 1f;

        protected GhostEmote(Entity tracking)
            : base(Vector2.Zero) {
            Tracking = tracking;

            Tag = TagsExt.SubHUD | Tags.Persistent | Tags.PauseUpdate | Tags.TransitionUpdate;
        }

        public GhostEmote(Entity tracking, string value)
            : this(tracking) {
            Value = value;
        }

        public override void Update() {
            base.Update();

            if (Scene is not Level level)
                return;

            if (Tracking?.Scene != Scene)
                PopOut = true;

            if (PopIn || FadeOut || PopOut) {
                AnimationTime += Engine.RawDeltaTime;
                if (AnimationTime < 0.1f && PopIn) {
                    float t = AnimationTime / 0.1f;
                    // Pop in.
                    PopupAlpha = Ease.CubeOut(t);
                    PopupScale = Ease.ElasticOut(t);

                } else if (AnimationTime < 1f) {
                    // Stay.
                    PopupAlpha = 1f;
                    PopupScale = 1f;

                } else if (FadeOut) {
                    // Fade out.
                    if (AnimationTime < 2f) {
                        float t = AnimationTime - 1f;
                        PopupAlpha = 1f - Ease.CubeIn(t);
                        PopupScale = 1f - 0.4f * Ease.CubeIn(t);

                    } else {
                        RemoveSelf();
                        return;
                    }

                } else if (PopOut) {
                    // Pop out.
                    if (AnimationTime < 1.1f) {
                        float t = (AnimationTime - 1f) / 0.1f;
                        PopupAlpha = 1f - Ease.CubeIn(t);
                        PopupAlpha *= PopupAlpha;
                        PopupScale = 1f - 0.4f * Ease.BounceIn(t);

                    } else {
                        RemoveSelf();
                        return;
                    }

                } else {
                    AnimationTime = 1f;
                    PopupAlpha = 1f;
                    PopupScale = 1f;
                }
            }

            Time += Engine.RawDeltaTime;

            if (Tracking == null)
                return;

            Position = Tracking.Position;
            // - name offset - popup offset
            Position.Y -= 16f + 6f;
        }

        public override void Render() {
            base.Render();

            if (Scene is not Level level)
                return;

            float popupScale = PopupScale * level.GetScreenScale();

            MTexture icon = null;
            string text = null;

            if (IsIcon(Value))
                icon = GetIcon(Value, Time);
            else
                text = Value;

            if (icon == null && string.IsNullOrWhiteSpace(text))
                return;

            if (Tracking == null)
                return;

            float screenMargins = 0f;
            Vector2 marginSize;

            if (icon != null) {
                marginSize = new(icon.Width, icon.Height);
                screenMargins = CelesteNetClientModule.Settings.InGameHUD.ScreenMargins * 16f;
            } else {
                marginSize = CelesteNetClientFont.Measure(text);
            }

            float scale = Size / Math.Max(marginSize.X, marginSize.Y) * 0.5f * popupScale;
            marginSize *= scale;
            marginSize.X *= 0.5f;

            bool isOnScreen = CelesteNetClientUtils.GetClampedScreenPos(
                Position,
                level,
                out Vector2 pos,
                marginX: screenMargins + marginSize.X,
                marginY: screenMargins + marginSize.Y,
                offsetX: 0f,
                offsetY: Float ? - (float) Math.Sin(Time * 2f) * 4f : 0f,
                false
            );

            if (!isOnScreen && CelesteNetClientModule.Settings.InGameHUD.OffScreenEmotes == CelesteNetClientSettings.OffScreenModes.Hidden)
                return;

            int opacity = CelesteNetClientModule.Settings.InGameHUD.EmoteOpacity;

            if (!isOnScreen && CelesteNetClientModule.Settings.InGameHUD.OffScreenEmotes == CelesteNetClientSettings.OffScreenModes.Opacity)
                opacity = CelesteNetClientModule.Settings.InGameHUD.OffScreenEmoteOpacity;

            float alpha = PopupAlpha * (opacity / 20f);

            if (alpha <= 0f)
                return;

            if (icon != null) {
                icon.DrawJustified(
                    pos,
                    new(0.5f, 1f),
                    Color.White * alpha,
                    Vector2.One * scale
                );
            } else {
                CelesteNetClientFont.DrawOutline(
                    text,
                    pos,
                    new(0.5f, 1f),
                    Vector2.One * scale,
                    Color.White * alpha,
                    2f,
                    Color.Black * alpha * alpha * alpha
                );
            }
        }

        public static bool IsText(string emote) {
            return !IsIcon(emote);
        }

        public static bool IsIcon(string emote) {
            return GetIconAtlas(ref emote) != null;
        }

        private static Atlas FallbackIconAtlas = new();
        public static Atlas GetIconAtlas(ref string emote) {
            if (emote.StartsWith("i:")) {
                emote = emote.Substring(2);
                return GFX.Gui ?? FallbackIconAtlas;
            }

            if (emote.StartsWith("g:")) {
                emote = emote.Substring(2);
                return GFX.Game ?? FallbackIconAtlas;
            }

            if (emote.StartsWith("p:")) {
                emote = emote.Substring(2);
                return GFX.Portraits ?? FallbackIconAtlas;
            }

            return null;
        }

        public static MTexture GetIcon(string emote, float time) {
            Atlas atlas;
            if ((atlas = GetIconAtlas(ref emote)) == null)
                return null;

            List<string> iconPaths = new(emote.Split(IconPathsSeperator));
            if (iconPaths.Count > 1 && int.TryParse(iconPaths[0], out int fps)) {
                iconPaths.RemoveAt(0);
            } else {
                fps = 7; // Default FPS.
            }

            List<MTexture> icons = iconPaths.SelectMany(iconPath => {
                iconPath = iconPath.Trim();
                List<MTexture> subs = atlas.orig_GetAtlasSubtextures(iconPath);
                if (subs.Count != 0)
                    return subs;
                if (atlas.Has(iconPath))
                    return new List<MTexture>() { atlas[iconPath] };
                if (iconPath.ToLowerInvariant() == "end")
                    return new List<MTexture>() { null };
                return new List<MTexture>();
            }).ToList();

            if (icons.Count == 0)
                return null;

            if (icons.Count == 1)
                return icons[0];

            int index = (int) Math.Floor(time * fps);

            if (index >= icons.Count - 1 && icons[icons.Count - 1] == null)
                return icons[icons.Count - 2];

            return icons[index % icons.Count];
        }

    }
}
