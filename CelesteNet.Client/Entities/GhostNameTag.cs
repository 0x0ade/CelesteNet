using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    public class GhostNameTag : Entity {

        public Entity Tracking;
        public string Name;

        protected Camera Camera;

        public Color Color = Color.White;
        public float Alpha = 1f;

        public GhostNameTag(Entity tracking, string name)
            : base(Vector2.Zero) {
            Tracking = tracking;
            Name = name;

            Tag = TagsExt.SubHUD | Tags.Persistent | Tags.PauseUpdate | Tags.TransitionUpdate;
        }

        public override void Update() {
            base.Update();

            if (Tracking != null && Tracking.Scene == null)
                RemoveSelf();
        }

        public override void Render() {
            base.Render();

            if (string.IsNullOrWhiteSpace(Name))
                return;

            Level level = SceneAs<Level>();
            if (level == null)
                return;

            float scale = level.GetScreenScale();

            Vector2 marginSize = CelesteNetClientFont.Measure(Name) * scale;
            marginSize.X *= 0.25f;
            marginSize.Y *= 0.5f;

            float screenMargins = CelesteNetClientModule.Settings.InGameHUD.ScreenMargins * 8f;

            // TODO: revert this change -- only reintroducing this for GravityHelper's ILHook to the "16"
            Vector2 worldpos = Tracking?.Position ?? Position;
            worldpos.Y -= 16f;

            bool isOnScreen = CelesteNetClientUtils.GetClampedScreenPos(
                worldpos,
                level,
                out Vector2 pos,
                marginLeft:   screenMargins + marginSize.X,
                marginTop:    screenMargins + marginSize.Y,
                marginRight:  screenMargins + marginSize.X,
                marginBottom: screenMargins
            );

            int opacity = CelesteNetClientModule.Settings.InGameHUD.NameOpacity;

            if (!isOnScreen && CelesteNetClientModule.Settings.InGameHUD.OffScreenNames == CelesteNetClientSettings.OffScreenModes.Hidden)
                return;

            if (!isOnScreen && CelesteNetClientModule.Settings.InGameHUD.OffScreenNames == CelesteNetClientSettings.OffScreenModes.Other)
                opacity = CelesteNetClientModule.Settings.InGameHUD.OffScreenNameOpacity;

            float a = Alpha * (opacity / 20f);

            if (a <= 0f)
                return;

            CelesteNetClientFont.DrawOutline(
                Name,
                pos,
                new(0.5f, 1f),
                Vector2.One * 0.5f * scale,
                Color * a,
                2f,
                Color.Black * (a * a * a)
            );
        }

    }
}
