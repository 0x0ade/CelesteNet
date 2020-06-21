using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetMainComponent : CelesteNetGameComponent {

        private Player Player;
        private Session Session;
        private bool WasIdle;

        public HashSet<string> ForceIdle = new HashSet<string>();
        public bool StateUpdated;

        public GhostNameTag PlayerNameTag;
        public GhostEmote PlayerIdleTag;
        public Dictionary<uint, Ghost> Ghosts = new Dictionary<uint, Ghost>();
        public Dictionary<uint, uint> FrameIDs = new Dictionary<uint, uint>();

        public CelesteNetMainComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10200;
            Visible = false;
        }

        public override void Initialize() {
            base.Initialize();

            On.Celeste.Level.LoadLevel += OnLoadLevel;
            Everest.Events.Level.OnExit += OnExitLevel;
            On.Celeste.PlayerHair.GetHairColor += OnGetHairColor;
            On.Celeste.PlayerHair.GetHairTexture += OnGetHairTexture;
            On.Celeste.Player.Play += OnPlayerPlayAudio;
        }

        public override void Start() {
            base.Start();

            if (Engine.Instance != null && Engine.Scene is Level level)
                OnLoadLevel(null, level, Player.IntroTypes.Transition, true);
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo player) {
            if (!Ghosts.TryGetValue(player.ID, out Ghost ghost) ||
                ghost == null)
                return;

            if (string.IsNullOrEmpty(player.FullName)) {
                ghost.NameTag.Name = "";
                Ghosts.Remove(player.ID);
                Client.Data.FreeOrder<DataPlayerFrame>(player.ID);
                return;
            }
        }

        public void Handle(CelesteNetConnection con, DataChannelMove move) {
            if (move.Player.ID == Client.PlayerInfo.ID) {
                foreach (Ghost ghost in Ghosts.Values)
                    ghost?.RemoveSelf();
                Ghosts.Clear();

                // The server resends all bound data anyway.
                foreach (DataPlayerInfo other in Client.Data.GetRefs<DataPlayerInfo>()) {
                    if (other.ID == Client.PlayerInfo.ID)
                        continue;

                    foreach (DataType data in Client.Data.GetBoundRefs(other))
                        if (data is IDataPlayerState state)
                            Client.Data.FreeRef(state.GetType(), state.ID);
                }

            } else {
                if (!Ghosts.TryGetValue(move.Player.ID, out Ghost ghost) ||
                    ghost == null)
                    return;

                ghost.NameTag.Name = "";
                Ghosts.Remove(move.Player.ID);

                foreach (DataType data in Client.Data.GetBoundRefs(move.Player))
                    if (data is IDataPlayerState state)
                        Client.Data.FreeRef(state.GetType(), state.ID);
            }
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            if (state.ID == Client.PlayerInfo.ID) {
                if (Player == null)
                    return;

                UpdateIdleTag(Player, ref PlayerIdleTag, state.Idle);

            } else {
                if (!Ghosts.TryGetValue(state.ID, out Ghost ghost) ||
                    ghost == null)
                    return;

                Session session = Session;
                if (session != null && (state.SID != session.Area.SID || state.Mode != session.Area.Mode)) {
                    ghost.NameTag.Name = "";
                    Ghosts.Remove(state.ID);
                    return;
                }

                UpdateIdleTag(ghost, ref ghost.IdleTag, state.Idle);
            }
        }

        public void Handle(CelesteNetConnection con, DataPlayerFrame frame) {
            Level level = Engine.Scene as Level;
            Session session = Session;

            bool outside =
                !Client.Data.TryGetBoundRef(frame.Player, out DataPlayerState state) ||
                level == null ||
                session == null ||
                state.SID != session.Area.SID ||
                state.Mode != session.Area.Mode;

            if (!Ghosts.TryGetValue(frame.Player.ID, out Ghost ghost) ||
                ghost == null ||
                ghost.Scene != level ||
                ghost.Sprite.Mode != frame.SpriteMode ||
                outside) {
                if (ghost != null)
                    ghost.NameTag.Name = "";
                ghost = null;
                Ghosts.Remove(frame.Player.ID);
            }

            if (level == null || outside)
                return;

            if (ghost == null) {
                Ghosts[frame.Player.ID] = ghost = new Ghost(frame.SpriteMode);
                level.Add(ghost);
            }

            ghost.NameTag.Name = frame.Player.FullName;
            UpdateIdleTag(ghost, ref ghost.IdleTag, state.Idle);
            ghost.UpdateSprite(frame.Position, frame.Scale, frame.Facing, frame.Color, frame.SpriteRate, frame.SpriteJustify, frame.CurrentAnimationID, frame.CurrentAnimationFrame);
            ghost.UpdateHair(frame.Facing, frame.HairColor, frame.HairSimulateMotion, frame.HairCount, frame.HairColors, frame.HairTextures);
            bool dead = ghost.Dead;
            ghost.Dead = frame.Dead && state.Level == session.Level;

            if (ghost.Dead != dead && ghost.Dead) {
                ghost.HandleDeath();
            }
        }

        public void Handle(CelesteNetConnection con, DataAudioPlay audio) {
            if (audio.Position == null) {
                Audio.Play(audio.Sound, audio.Param, audio.Value);
                return;
            }

            if (audio.Player != null) {
                Session session = Session;
                if (!Client.Data.TryGetBoundRef(audio.Player, out DataPlayerState state) ||
                    session == null ||
                    state.SID != session.Area.SID ||
                    state.Mode != session.Area.Mode ||
                    state.Level != session.Level)
                    return;
            }

            Audio.Play(audio.Sound, audio.Position.Value, audio.Param, audio.Value);
        }

        public void UpdateIdleTag(Entity target, ref GhostEmote idleTag, bool idle) {
            if (idle && idleTag == null) {
                Engine.Scene.Add(idleTag = new GhostEmote(target, "i:hover/idle") {
                    PopIn = true,
                    Float = true
                });

            } else if (!idle && idleTag != null) {
                idleTag.PopOut = true;
                idleTag.AnimationTime = 1f;
                idleTag = null;
            }
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (Client == null || !Client.IsReady)
                return;

            if (!(Engine.Scene is Level level)) {
                if (Player != null && Engine.Scene != Player.Scene) {
                    Player = null;
                    Session = null;
                    WasIdle = false;
                    SendState();
                }
                return;
            }

            bool sendState = StateUpdated;
            StateUpdated = false;

            if (Player == null || Player.Scene != Engine.Scene) {
                Player = level.Tracker.GetEntity<Player>();
                if (Player != null) {
                    Session = level.Session;
                    WasIdle = false;
                    sendState = true;
                }
            }

            bool idle = level.FrozenOrPaused || level.Overlay != null;
            if (WasIdle != idle) {
                WasIdle = idle;
                sendState = true;
            }

            if (sendState)
                SendState();

            if (Player == null)
                return;

            if (PlayerNameTag == null || PlayerNameTag.Tracking != Player) {
                PlayerNameTag?.RemoveSelf();
                level.Add(PlayerNameTag = new GhostNameTag(Player, Client.PlayerInfo.FullName));
            }

            SendFrame();
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            MainThreadHelper.Do(() => {
                On.Celeste.Level.LoadLevel -= OnLoadLevel;
                Everest.Events.Level.OnExit -= OnExitLevel;
                On.Celeste.PlayerHair.GetHairColor -= OnGetHairColor;
                On.Celeste.PlayerHair.GetHairTexture -= OnGetHairTexture;
                On.Celeste.Player.Play -= OnPlayerPlayAudio;
            });

            Cleanup();
        }

        public void Cleanup() {
            Player = null;
            Session = null;
            WasIdle = false;

            foreach (Ghost ghost in Ghosts.Values)
                ghost?.RemoveSelf();
            Ghosts.Clear();

            if (PlayerNameTag != null)
                PlayerNameTag.Name = "";

            if (PlayerIdleTag != null) {
                PlayerIdleTag.PopOut = true;
                PlayerIdleTag.AnimationTime = 1f;
            }
        }

        #region Hooks

        public void OnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level level, Player.IntroTypes playerIntro, bool isFromLoader = false) {
            orig?.Invoke(level, playerIntro, isFromLoader);

            Session = level.Session;
            WasIdle = false;

            if (Client == null)
                return;

            Player = level.Tracker.GetEntity<Player>();

            SendState();
        }

        public void OnExitLevel(Level level, LevelExit exit, LevelExit.Mode mode, Session session, HiresSnow snow) {
            Session = null;
            WasIdle = false;

            Cleanup();

            SendState();
        }

        public Color OnGetHairColor(On.Celeste.PlayerHair.orig_GetHairColor orig, PlayerHair self, int index) {
            if (self.Entity is Ghost ghost && 0 <= index && index < ghost.HairColors.Length)
                return ghost.HairColors[index] * ghost.Alpha;
            return orig(self, index);
        }

        private MTexture OnGetHairTexture(On.Celeste.PlayerHair.orig_GetHairTexture orig, PlayerHair self, int index) {
            if (self.Entity is Ghost ghost && 0 <= index && index < ghost.HairTextures.Length && GFX.Game.Textures.TryGetValue(ghost.HairTextures[index], out MTexture tex))
                return tex;
            return orig(self, index);
        }

        private EventInstance OnPlayerPlayAudio(On.Celeste.Player.orig_Play orig, Player self, string sound, string param, float value) {
            SendAudioPlay(self.Center, sound, param, value);
            return orig(self, sound, param, value);
        }

        #endregion


        #region Send

        public void SendState() {
            try {
                Client?.SendAndHandle(new DataPlayerState {
                    Player = Client.PlayerInfo,
                    SID = Session?.Area.GetSID() ?? "",
                    Mode = Session?.Area.Mode ?? AreaMode.Normal,
                    Level = Session?.Level ?? "",
                    Idle = ForceIdle.Count != 0 || (Player?.Scene is Level level && (level.FrozenOrPaused || level.Overlay != null))
                });
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendState:\n{e}");
                Context.Dispose();
            }
        }

        public void SendFrame() {
            if (Player == null || Player.Sprite == null || Player.Hair == null)
                return;

            int hairCount = Player.Sprite.HairCount;
            Color[] hairColors = new Color[hairCount];
            for (int i = 0; i < hairCount; i++)
                hairColors[i] = Player.Hair.GetHairColor(i);
            string[] hairTextures = new string[hairCount];
            for (int i = 0; i < hairCount; i++)
                hairTextures[i] = Player.Hair.GetHairTexture(i).AtlasPath;

            try {
                Client?.Send(new DataPlayerFrame {
                    Position = Player.Position,
                    Speed = Player.Speed,
                    Scale = Player.Sprite.Scale,
                    Color = Player.Sprite.Color,
                    Facing = Player.Facing,

                    SpriteMode = Player.Sprite.Mode,
                    CurrentAnimationID = Player.Sprite.CurrentAnimationID,
                    CurrentAnimationFrame = Player.Sprite.CurrentAnimationFrame,

                    HairColor = Player.Hair.Color,
                    HairSimulateMotion = Player.Hair.SimulateMotion,

                    HairCount = (byte) hairCount,
                    HairColors = hairColors,
                    HairTextures = hairTextures,

                    DashColor = Player.StateMachine.State == Player.StDash ? Player.GetCurrentTrailColor() : (Color?) null,
                    DashDir = Player.DashDir,
                    DashWasB = Player.GetWasDashB(),

                    Dead = Player.Dead
                });
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendFrame:\n{e}");
                Context.Dispose();
            }
        }

        public void SendAudioPlay(Vector2 pos, string sound, string param = null, float value = 0f) {
            try {
                Client?.Send(new DataAudioPlay {
                    Player = Client.PlayerInfo,
                    Sound = sound,
                    Param = param ?? "",
                    Value = value,
                    Position = pos
                });
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendAudioPlay:\n{e}");
                Context.Dispose();
            }
        }

        #endregion

    }
}
