using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    public class GhostNameTag : Entity {

        public Entity Tracking;
        public string Name;

        protected Camera Camera;

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

            float a = Alpha * (CelesteNetClientModule.Settings.NameOpacity / 4f);

            if (a <= 0f || string.IsNullOrWhiteSpace(Name))
                return;

            Level level = SceneAs<Level>();
            if (level == null)
                return;

            float scale = level.GetScreenScale();

            Vector2 pos = Tracking?.Position ?? Position;
            pos.Y -= 16f;

            pos = level.WorldToScreen(pos);

            Vector2 size = CelesteNetClientFont.Measure(Name) * scale;
            pos = pos.Clamp(
                0f + size.X * 0.25f + 32f, 0f + size.Y * 0.5f + 32f,
                1920f - size.X * 0.25f - 32f, 1080f - 32f
            );

            CelesteNetClientFont.DrawOutline(
                Name,
                pos,
                new Vector2(0.5f, 1f),
                Vector2.One * 0.5f * scale,
                Color.White * a,
                2f,
                Color.Black * (a * a * a)
            );
        }

    }
}
