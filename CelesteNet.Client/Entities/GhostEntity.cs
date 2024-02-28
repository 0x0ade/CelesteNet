using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client.Entities
{
    public class GhostEntity : Entity {

        public Ghost Ghost;

        public string SpriteID;
        public Sprite Sprite;
        public CelesteNetClientSpriteDB.SpriteMeta SpriteMeta;

        public GhostEntity(Ghost ghost)
            : base(Vector2.Zero) {
            Ghost = ghost;

            Depth = -1000000;

            Tag = Tags.Persistent | Tags.PauseUpdate | Tags.TransitionUpdate;
        }

        public override void Update() {
            if (Ghost == null || string.IsNullOrEmpty(Ghost.NameTag.Name) || Ghost.Scene != Scene) {
                RemoveSelf();
                return;
            }

            base.Update();
        }

        public void UpdateSprite(Vector2 scale, int depth, Color color, float rate, Vector2? justify, string spriteID, string animationID, int animationFrame) {
            if (spriteID != SpriteID) {
                if (Sprite != null)
                    Remove(Sprite);

                SpriteID = spriteID;
                try {
                    Sprite = GFX.SpriteBank.Create(spriteID);
                } catch {
                    Sprite = GFX.SpriteBank.Create("flutterBird");
                }
                SpriteMeta = Sprite.GetMeta();

                Add(Sprite);
            }

            Sprite.Scale = scale;

            Depth = depth;

            Sprite.Color = color * Ghost.Alpha;

            Sprite.Rate = rate;
            Sprite.Justify = justify;

            if (!string.IsNullOrEmpty(animationID)) {
                try {
                    if (Sprite.CurrentAnimationID != animationID)
                        Sprite.Play(animationID);
                    Sprite.SetAnimationFrame(animationFrame);
                } catch {
                    // Play likes to fail randomly as the ID doesn't exist in an underlying dict.
                    // Let's ignore this for now.
                }
            }
        }

        public override void Render() {
            Sprite s = Sprite;
            if (s != null && (SpriteMeta?.ForceOutline ?? false)) {
                Vector2 pos = s.Position;
                Color color = s.Color;
                float a = Ghost?.Alpha ?? 1f;
                s.Color = Color.Black * a * a;
                s.Position = pos + new Vector2(-1f, 0f);
                s.Render();
                s.Position = pos + new Vector2(0f, -1f);
                s.Render();
                s.Position = pos + new Vector2(1f, 0f);
                s.Render();
                s.Position = pos + new Vector2(0f, 1f);
                s.Render();
                s.Position = pos;
                s.Color = color;
            }

            base.Render();
        }

    }
}
