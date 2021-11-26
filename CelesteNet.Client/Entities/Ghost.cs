using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    [Tracked]
    public class Ghost : Actor, ITickReceiver {

        public CelesteNetClientContext Context;

        public DataPlayerInfo PlayerInfo;
        public volatile DataPlayerGraphics PlayerGraphics;

        public float Alpha = 0.875f;

        public Vector2 Speed;

        public PlayerSprite Sprite;
        public PlayerHair Hair;
        public Leader Leader;
        public Holdable Holdable;
        private bool HoldableAdded;

        public GhostNameTag NameTag;
        public GhostEmote IdleTag;

        public Color[] HairColors = new[] { Color.White };

        public (bool wasB, Vector2 dir)? Dash;

        public bool Dead;

        public List<GhostFollower> Followers = new();
        public GhostEntity Holding;

        public bool Interactive;

        public float GrabCooldown = 0f;
        public const float GrabCooldownMax = CelesteNetMainComponent.GrabCooldownMax;
        protected DataPlayerGrabPlayer GrabPacket;

        // TODO Revert this to Queue<> once MonoKickstart weirdness is fixed
        protected List<Action<Ghost>> UpdateQueue = new();
        protected bool IsUpdating;

        public Ghost(CelesteNetClientContext context, DataPlayerInfo playerInfo, PlayerSpriteMode spriteMode)
            : base(Vector2.Zero) {
            Context = context;
            PlayerInfo = playerInfo;

            Depth = 0;

            RetryPlayerSprite:
            try {
                Sprite = new(spriteMode | (PlayerSpriteMode) (1 << 31));
            } catch (Exception) {
                if (spriteMode != PlayerSpriteMode.Madeline) {
                    spriteMode = PlayerSpriteMode.Madeline;
                    goto RetryPlayerSprite;
                }
                throw;
            }

            Add(Hair = new(Sprite));
            Add(Sprite);
            Hair.Color = Player.NormalHairColor;
            Add(Leader = new(new(0f, -8f)));
            Holdable = new() {
                OnCarry = OnCarry,
                OnRelease = OnRelease
            };

            Collidable = true;
            Collider = new Hitbox(8f, 11f, -4f, -11f);
            Add(new PlayerCollider(OnPlayer));

            NameTag = new(this, "");
            NameTag.Alpha = 0.85f;

            Dead = false;
            AllowPushing = false;
            SquishCallback = null;

            Tag = Tags.Persistent | Tags.PauseUpdate | Tags.TransitionUpdate;
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
            if (!Interactive || GrabCooldown > 0f || !CelesteNetClientModule.Settings.Interactions || Context?.Main.GrabbedBy == this)
                return;

            if (player.StateMachine.State == Player.StNormal &&
                player.Speed.Y > 0f && player.Bottom <= Top + 3f) {

                Dust.Burst(player.BottomCenter, -1.57079637f, 8);
                (Scene as Level)?.DirectionalShake(Vector2.UnitY, 0.05f);
                Input.Rumble(RumbleStrength.Light, RumbleLength.Medium);
                player.Bounce(Top + 2f);
                player.Play("event:/game/general/thing_booped");

            } else if (player.StateMachine.State != Player.StDash &&
                player.StateMachine.State != Player.StRedDash &&
                player.StateMachine.State != Player.StDreamDash &&
                player.StateMachine.State != Player.StBirdDashTutorial &&
                player.Speed.Y <= 0f && Bottom <= player.Top + 5f) {
                player.Speed.Y = Math.Max(player.Speed.Y, 16f);
            }
        }

        public void OnCarry(Vector2 position) {
            if (!Interactive || GrabCooldown > 0f || !CelesteNetClientModule.Settings.Interactions || IdleTag != null)
                return;

            Position = position;
            Collidable = false;

            CelesteNetClient client = Context?.Client;
            if (PlayerInfo == null || client == null)
                return;

            GrabPacket = new DataPlayerGrabPlayer {
                Player = client.PlayerInfo,
                Grabbing = PlayerInfo,
                Position = position,
                Force = null
            };
        }

        public void OnRelease(Vector2 force) {
            Collidable = true;

            if (!Interactive || GrabCooldown > 0f || !CelesteNetClientModule.Settings.Interactions || IdleTag != null)
                return;

            CelesteNetClient client = Context?.Client;
            if (PlayerInfo == null || client == null)
                return;

            GrabPacket = new DataPlayerGrabPlayer {
                Player = client.PlayerInfo,
                Grabbing = PlayerInfo,
                Position = Position,
                Force = force
            };
        }

        public override void Update() {
            lock (UpdateQueue) {
                IsUpdating = true;
                while (UpdateQueue.Count > 0) {
                    Action<Ghost> act = UpdateQueue[0];
                    UpdateQueue.RemoveAt(0);
                    act(this);
                }
                IsUpdating = false;
            }

            if (string.IsNullOrEmpty(NameTag.Name) && Active) {
                RemoveSelf();
                return;
            }

            bool holdable = Interactive && GrabCooldown <= 0f && CelesteNetClientModule.Settings.Interactions && IdleTag == null;

            GrabCooldown -= Engine.RawDeltaTime;
            if (GrabCooldown < 0f)
                GrabCooldown = 0f;

            if (!holdable && Holdable.Holder != null) {
                Collidable = false;
                Holdable.Release(Vector2.Zero);
                Collidable = true;
            }

            if (!HoldableAdded && holdable) {
                HoldableAdded = true;
                Add(Holdable);
            } else if (HoldableAdded && !holdable && !Holdable.IsHeld) {
                HoldableAdded = false;
                Remove(Holdable);
            }
            if (CelesteNetClientModule.Settings.PlayerOpacity == 0) {
                Alpha = 0f;
            } else {
                Alpha = 0.875f * ((CelesteNetClientModule.Settings.PlayerOpacity + 2) / 6f);
            }

            if (NameTag.Scene != Scene)
                Scene.Add(NameTag);

            Visible = !Dead;

            base.Update();

            if (Scene is not Level level)
                return;

            if (!level.GetUpdateHair() || level.Overlay is PauseUpdateOverlay)
                Hair.AfterUpdate();

            foreach (GhostFollower gf in Followers)
                if (gf.Scene != level)
                    level.Add(gf);

            if (Holding != null && Holding.Scene != level)
                level.Add(Holding);

            // TODO: Get rid of this, sync particles separately!
            if (Dash != null && level != null && Speed != Vector2.Zero && level.OnRawInterval(0.02f))
                level.ParticlesFG.Emit(Dash.Value.wasB ? Player.P_DashB : Player.P_DashA, Center + Calc.Random.Range(Vector2.One * -2f, Vector2.One * 2f), Dash.Value.dir.Angle());
        }

        public void Tick() {
            if (GrabPacket != null) {
                CelesteNetClient client = Context?.Client;
                if (client != null) {
                    try {
                        client.Send(GrabPacket);
                    } catch (Exception e) {
                        Logger.Log(LogLevel.INF, "client-ghost", $"Error sending grab packet: {e}");
                        Context.Dispose();
                    }
                }
            }
            GrabPacket = null;
        }

        public void RunOnUpdate(Action<Ghost> action, bool wait = false) {
            bool updating;
            lock (UpdateQueue)
                updating = IsUpdating;
            if (updating) {
                action(this);
                return;
            }

            using ManualResetEvent waiter = wait ? new ManualResetEvent(false) : null;
            if (wait) {
                Action<Ghost> real = action;
                action = g => {
                    try {
                        real(g);
                    } finally {
                        waiter.Set();
                    }
                };
            }

            lock (UpdateQueue)
                UpdateQueue.Add(action);

            if (wait)
                WaitHandle.WaitAny(new WaitHandle[] { waiter });
        }

        public void UpdateGraphics(DataPlayerGraphics graphics) {
            if (graphics.HairCount == 0) {
                graphics.HairCount = 1;
                graphics.HairScales = new[] { Vector2.One };
                graphics.HairTextures = new[] { "characters/player/hair00" };
            }

            PlayerGraphics = graphics;

            Depth = graphics.Depth + 1;
            Sprite.Color = graphics.SpriteColor * Alpha;
            Sprite.Rate = graphics.SpriteRate;

            Sprite.HairCount = graphics.HairCount;
            Hair.StepPerSegment = graphics.HairStepPerSegment;
            Hair.StepInFacingPerSegment = graphics.HairStepInFacingPerSegment;
            Hair.StepApproach = graphics.HairStepApproach;
            Hair.StepYSinePerSegment = graphics.HairStepYSinePerSegment;
            while (Hair.Nodes.Count < graphics.HairCount)
                Hair.Nodes.Add(Hair.Nodes.LastOrDefault());
            while (Hair.Nodes.Count > graphics.HairCount)
                Hair.Nodes.RemoveAt(Hair.Nodes.Count - 1);
            Hair.Alpha = Alpha;
        }

        public void UpdateAnimation(int animationID, int animationFrame) {
            if (PlayerGraphics == null)
                return;

            if (animationID < 0 || PlayerGraphics.SpriteAnimations.Length <= animationID)
                return;

            string strID = PlayerGraphics.SpriteAnimations[animationID];
            if (strID != null && Sprite.Animations.ContainsKey(strID)) {
                if (Sprite.CurrentAnimationID != strID)
                    Sprite.Play(strID);
                Sprite.SetAnimationFrame(animationFrame);
            }
        }

        public void UpdatePosition(Vector2 pos, Vector2 scale, Facings facing, Vector2 speed) {
            if (Holdable.Holder == null)
                Position = pos;
            Sprite.Scale = scale;
            Sprite.Scale.X *= (float) facing;
            Hair.Facing = facing;
            Speed = speed;
        }
        
        public void UpdateHair(Facings facing, Color[] colors, string texture0, bool simulateMotion) {
            if (PlayerGraphics == null)
                return;

            if (colors.Length <= 0)
                colors = new[] { Color.White };
            if (PlayerGraphics.HairCount < colors.Length)
                Array.Resize(ref colors, PlayerGraphics.HairCount);

            Hair.Facing = facing;
            HairColors = colors;
            PlayerGraphics.HairTextures[0] = texture0;
            Hair.SimulateMotion = simulateMotion;
        }

        public void UpdateDash((bool wasB, Vector2 dir)? dash) {
            Dash = dash;
        }

        public void UpdateDead(bool dead) {
            if (!Dead && dead)
                HandleDeath();
            Dead = dead;
        }

        public void UpdateFollowers(DataPlayerFrame.Entity[] followers) {
            for (int i = 0; i < followers.Length; i++) {
                DataPlayerFrame.Entity f = followers[i];
                GhostFollower gf;
                if (i >= Followers.Count) {
                    gf = new(this);
                    gf.Position = Position + Leader.Position;
                    Followers.Add(gf);
                } else {
                    gf = Followers[i];
                }
                gf.UpdateSprite(f.Scale, f.Depth, f.Color, f.SpriteRate, f.SpriteJustify, f.SpriteID, f.CurrentAnimationID, f.CurrentAnimationFrame);
            }

            while (Followers.Count > followers.Length) {
                GhostFollower gf = Followers[Followers.Count - 1];
                gf.Ghost = null;
                Followers.RemoveAt(Followers.Count - 1);
            }
        }

        public void UpdateHolding(DataPlayerFrame.Entity h) {
            if (h == null) {
                if (Holding != null)
                    Holding.Ghost = null;
                Holding = null;
                return;
            }

            if (Holding == null)
                Holding = new(this);
            Holding.Position = h.Position;
            Holding.UpdateSprite(h.Scale, h.Depth, h.Color, h.SpriteRate, h.SpriteJustify, h.SpriteID, h.CurrentAnimationID, h.CurrentAnimationFrame);
        }


        public void HandleDeath() {
            if (Scene is not Level level ||
                level.Paused || level.Overlay != null)
                return;
            level.Add(new GhostDeadBody(this, Vector2.Zero));
        }

    }
}
