using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    public class GhostEntity : Entity {

        public Ghost Ghost;

        public string SpriteID;
        public Sprite Sprite;

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
                } catch (Exception) {
                    Sprite = GFX.SpriteBank.Create("flutterBird");
                }

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

    }
}
