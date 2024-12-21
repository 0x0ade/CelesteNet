using Microsoft.Xna.Framework;
using Monocle;
using System.Collections;

namespace Celeste.Mod.CelesteNet.Client.Entities
{
    public class GhostDeadBody : Entity {
        private Color initialHairColor;

        private Vector2 bounce = Vector2.Zero;

        private Ghost player;

        private PlayerHair hair;

        private PlayerSprite sprite;

        private DeathEffect? deathEffect;

        private Facings facing;

        private float scale = 1f;

        private float Alpha = 1f;

        public GhostDeadBody(Ghost player, Vector2 direction, float alpha = 1f) {
            Depth = -1000000;
            this.player = player;
            facing = player.Hair.Facing;
            Position = player.Position;
            Add(hair = player.Hair);
            Add(sprite = player.Sprite);
            sprite.Color = Color.White * alpha;
            initialHairColor = hair.Color * alpha;
            bounce = direction;
            Alpha = alpha;
            hair.Alpha = Alpha;
            Add(new Coroutine(DeathRoutine()));
            Tag = Tags.PauseUpdate | Tags.TransitionUpdate;
        }

        private IEnumerator DeathRoutine() {
            Level level = SceneAs<Level>();
            Position += Vector2.UnitY * -5f;
            level.Displacement.AddBurst(Position, 0.3f, 0f, 80f, Alpha);
            player.Context?.Main?.PlayAudio(player, "event:/char/madeline/death", Position);
            Add(deathEffect = new(initialHairColor, Center - Position));
            yield return deathEffect.Duration * 1.0f;
            player.RemoveSelf();
            RemoveSelf();
        }

        public override void Update() {
            base.Update();
            hair.Color = sprite.CurrentAnimationFrame == 0 ? sprite.Color : initialHairColor;
        }

        public override void Render() {
            if (deathEffect == null) {
                sprite.Scale.X = (float) facing * scale;
                sprite.Scale.Y = scale;
                hair.Facing = facing;
                base.Render();
            } else {
                deathEffect.Render();
            }
        }
    }
}
