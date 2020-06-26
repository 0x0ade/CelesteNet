using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    public class Ghost : Actor {

        public CelesteNetClientComponent Context;

        public float Alpha = 0.875f;

        public Vector2 Speed;

        public PlayerSprite Sprite;
        public PlayerHair Hair;

        public GhostNameTag NameTag;
        public GhostEmote IdleTag;

        public Color[] HairColors;
        public string[] HairTextures;

        public bool? DashWasB;
        public Vector2 DashDir;

        public bool Dead;

        public Ghost(CelesteNetClientComponent context, PlayerSpriteMode spriteMode)
            : base(Vector2.Zero) {
            Context = context;

            Depth = 0;

            Sprite = new PlayerSprite(spriteMode);
            Add(Hair = new PlayerHair(Sprite));
            Add(Sprite);
            Hair.Color = Player.NormalHairColor;

            Collidable = true;
            Collider = new Hitbox(8f, 11f, -4f, -11f);
            Add(new PlayerCollider(OnPlayer));

            NameTag = new GhostNameTag(this, "");

            Dead = false;

            Tag = Tags.Persistent | Tags.PauseUpdate;
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

        public void OnPlayer(Player player) {
            if (!CelesteNetClientModule.Settings.Collision)
                return;

            if (player.StateMachine.State == Player.StNormal &&
                player.Speed.Y > 0f && player.Bottom <= Top + 3f) {

                Dust.Burst(player.BottomCenter, -1.57079637f, 8);
                (Scene as Level)?.DirectionalShake(Vector2.UnitY, 0.05f);
                Input.Rumble(RumbleStrength.Light, RumbleLength.Medium);
                player.Bounce(Top + 2f);
                player.Play("event:/game/general/thing_booped");

            } else if (player.Speed.Y <= 0f && Bottom <= player.Top + 5f) {
                player.Speed.Y = Math.Max(player.Speed.Y, 16f);
            }
        }

        public override void Update() {
            if (string.IsNullOrEmpty(NameTag.Name))
                RemoveSelf();

            Visible = !Dead;

            base.Update();

            if (!(Scene is Level level))
                return;

            if (!level.GetUpdateHair() || level.Overlay is PauseUpdateOverlay)
                Hair.AfterUpdate();

            // TODO: Get rid of this, sync particles separately!
            if (DashWasB != null && level != null && Speed != Vector2.Zero && level.OnRawInterval(0.02f))
                level.ParticlesFG.Emit(DashWasB.Value ? Player.P_DashB : Player.P_DashA, Center + Calc.Random.Range(Vector2.One * -2f, Vector2.One * 2f), DashDir.Angle());
        }

        public void UpdateHair(Facings facing, Color color, bool simulateMotion, int count, Color[] colors, string[] textures) {
            Hair.Facing = facing;
            Hair.Color = color * Alpha;
            Hair.Alpha = Alpha;
            Hair.SimulateMotion = simulateMotion;
            Sprite.HairCount = count;
            while (Hair.Nodes.Count < count)
                Hair.Nodes.Add(Hair.Nodes.LastOrDefault());
            while (Hair.Nodes.Count > count)
                Hair.Nodes.RemoveAt(Hair.Nodes.Count - 1);
            HairColors = colors;
            HairTextures = textures;
        }

        public void UpdateSprite(Vector2 position, Vector2 speed, Vector2 scale, Facings facing, int depth, Color color, float rate, Vector2? justify, string animationID, int animationFrame) {
            Position = position;
            Speed = speed;

            Sprite.Scale = scale;
            Sprite.Scale.X *= (float) facing;

            Depth = depth;

            Sprite.Color = color * Alpha;

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

        public void UpdateDash(bool? wasB, Vector2 dir) {
            DashWasB = wasB;
            DashDir = dir;
        }

        public void UpdateDead(bool dead) {
            if (!Dead && dead)
                HandleDeath();
            Dead = dead;
        }

        public void HandleDeath() {
            Scene.Add(new GhostDeadBody(this, Vector2.Zero));
        }

    }
}
