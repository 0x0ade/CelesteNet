using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    public class Ghost : Actor {

        public PlayerSprite Sprite;
        public PlayerHair Hair;

        public GhostNameTag NameTag;

        internal Color[] HairColors;
        internal string[] HairTextures;

        public Ghost(PlayerSpriteMode spriteMode)
            : base(Vector2.Zero) {
            Depth = 1;

            Sprite = new PlayerSprite(spriteMode);
            Add(Hair = new PlayerHair(Sprite));
            Add(Sprite);

            Hair.Color = Player.NormalHairColor;

            NameTag = new GhostNameTag(this, "");
        }

        public override void Added(Scene scene) {
            base.Added(scene);

            Hair.Start();
            Scene.Add(NameTag);
        }

        public override void Removed(Scene scene) {
            base.Removed(scene);

            NameTag.RemoveSelf();
        }

        public void UpdateHair(Facings facing, Color color, bool simulateMotion, int count, Color[] colors, string[] textures) {
            Hair.Facing = facing;
            Hair.Color = color;
            Hair.SimulateMotion = simulateMotion;
            Sprite.HairCount = count;
            HairColors = colors;
            HairTextures = textures;
        }

        public void UpdateSprite(Vector2 position, Vector2 scale, Facings facing, Color color, float rate, Vector2? justify, string animationID, int animationFrame) {
            Position = position;
            Sprite.Scale = scale;
            Sprite.Scale.X *= (float) facing;
            Sprite.Color = color;

            Sprite.Rate = rate;
            Sprite.Justify = justify;

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
