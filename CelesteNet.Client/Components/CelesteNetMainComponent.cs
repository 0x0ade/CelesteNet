using Celeste.Editor;
using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetMainComponent : CelesteNetGameComponent {

        public const string LevelDebugMap = ":celestenet_debugmap:";

        private static readonly FieldInfo f_MapEditor_area =
            typeof(MapEditor)
            .GetField("area", BindingFlags.NonPublic | BindingFlags.Static);

        private Player Player;
        private TrailManager TrailManager;
        private Session Session;
        private AreaKey? MapEditorArea;
        private bool WasIdle;

        public uint FrameNextID;

        public HashSet<string> ForceIdle = new HashSet<string>();
        public bool StateUpdated;

        public GhostNameTag PlayerNameTag;
        public GhostEmote PlayerIdleTag;
        public ConcurrentDictionary<uint, Ghost> Ghosts = new ConcurrentDictionary<uint, Ghost>();

        public HashSet<PlayerSpriteMode> UnsupportedSpriteModes = new HashSet<PlayerSpriteMode>();

        public Ghost GrabbedBy;
        public Vector2 GrabLastSpeed;
        public float GrabTimeout = 0f;
        public const float GrabTimeoutMax = 0.1f;

        public CelesteNetMainComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10200;
            Visible = false;
        }

        public override void Initialize() {
            base.Initialize();

            MainThreadHelper.Do(() => {
                using (new DetourContext("CelesteNetMain") {
                    Before = { "*" }
                }) {
                    On.Celeste.Level.LoadLevel += OnLoadLevel;
                    Everest.Events.Level.OnExit += OnExitLevel;
                    On.Celeste.PlayerHair.GetHairColor += OnGetHairColor;
                    On.Celeste.PlayerHair.GetHairTexture += OnGetHairTexture;
                    On.Celeste.Player.Play += OnPlayerPlayAudio;
                    On.Celeste.TrailManager.Add_Vector2_Image_PlayerHair_Vector2_Color_int_float_bool_bool += OnDashTrailAdd;
                    On.Celeste.PlayerSprite.ctor += OnPlayerSpriteCtor;
                }
            });
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            MainThreadHelper.Do(() => {
                On.Celeste.Level.LoadLevel -= OnLoadLevel;
                Everest.Events.Level.OnExit -= OnExitLevel;
                On.Celeste.PlayerHair.GetHairColor -= OnGetHairColor;
                On.Celeste.PlayerHair.GetHairTexture -= OnGetHairTexture;
                On.Celeste.Player.Play -= OnPlayerPlayAudio;
                On.Celeste.TrailManager.Add_Vector2_Image_PlayerHair_Vector2_Color_int_float_bool_bool -= OnDashTrailAdd;
                On.Celeste.PlayerSprite.ctor -= OnPlayerSpriteCtor;
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

        #region Handlers

        public void Handle(CelesteNetConnection con, DataPlayerInfo player) {
            if (player.ID == Client.PlayerInfo.ID) {
                if (PlayerNameTag != null)
                    PlayerNameTag.Name = player.DisplayName;
                return;
            }

            if (!Ghosts.TryGetValue(player.ID, out Ghost ghost) ||
                ghost == null)
                return;

            if (string.IsNullOrEmpty(player.DisplayName)) {
                ghost.RunOnUpdate(ghost => ghost.NameTag.Name = "");
                Ghosts.TryRemove(player.ID, out _);
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
                        if (data.TryGet(Client.Data, out MetaPlayerPrivateState state))
                            Client.Data.FreeBoundRef(data);
                }

            } else {
                if (!Ghosts.TryGetValue(move.Player.ID, out Ghost ghost) ||
                    ghost == null)
                    return;

                ghost.RunOnUpdate(ghost => ghost.NameTag.Name = "");
                Ghosts.TryRemove(move.Player.ID, out _);

                foreach (DataType data in Client.Data.GetBoundRefs(move.Player))
                    if (data.TryGet(Client.Data, out MetaPlayerPrivateState state))
                        Client.Data.FreeBoundRef(data);
            }
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            uint id = state.Player?.ID ?? uint.MaxValue;
            if (id == (Client?.PlayerInfo?.ID ?? uint.MaxValue)) {
                if (Player == null)
                    return;

                UpdateIdleTag(Player, ref PlayerIdleTag, state.Idle);

            } else {
                if (!Ghosts.TryGetValue(id, out Ghost ghost) ||
                    ghost == null)
                    return;

                Session session = Session;
                if (session != null && (state.SID != session.Area.SID || state.Mode != session.Area.Mode || state.Level == LevelDebugMap)) {
                    ghost.RunOnUpdate(ghost => ghost.NameTag.Name = "");
                    Ghosts.TryRemove(id, out _);
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
                state.Mode != session.Area.Mode ||
                state.Level == LevelDebugMap;

            if (UnsupportedSpriteModes.Contains(frame.SpriteMode))
                frame.SpriteMode = PlayerSpriteMode.Madeline;

            if (!Ghosts.TryGetValue(frame.Player.ID, out Ghost ghost) ||
                ghost == null ||
                (ghost.Active && ghost.Scene != level) ||
                ghost.Sprite.Mode != frame.SpriteMode ||
                outside) {
                ghost?.RunOnUpdate(ghost => ghost.NameTag.Name = "");
                ghost = null;
                Ghosts.TryRemove(frame.Player.ID, out _);
            }

            if (level == null || outside)
                return;

            if (ghost == null) {
                Ghosts[frame.Player.ID] = ghost = new Ghost(Context, frame.Player, frame.SpriteMode);
                ghost.Active = false;
                ghost.NameTag.Name = frame.Player.DisplayName;
                if (ghost.Sprite.Mode != frame.SpriteMode)
                    UnsupportedSpriteModes.Add(frame.SpriteMode);
                RunOnMainThread(() => {
                    level.Add(ghost);
                    level.OnEndOfFrame += () => ghost.Active = true;
                });
            }

            ghost.RunOnUpdate(ghost => {
                if (string.IsNullOrEmpty(ghost.NameTag.Name))
                    return;
                ghost.NameTag.Name = frame.Player.DisplayName;
                UpdateIdleTag(ghost, ref ghost.IdleTag, state.Idle);
                ghost.UpdateSprite(frame.Position, frame.Speed, frame.Scale, frame.Facing, frame.Depth, frame.Color, frame.SpriteRate, frame.SpriteJustify, frame.CurrentAnimationID, frame.CurrentAnimationFrame);
                ghost.UpdateHair(frame.Facing, frame.HairColor, frame.HairSimulateMotion, frame.HairCount, frame.HairColors, frame.HairTextures);
                ghost.UpdateDash(frame.DashWasB, frame.DashDir); // TODO: Get rid of this, sync particles separately!
                ghost.UpdateDead(frame.Dead && state.Level == session.Level);
                ghost.UpdateFollowers((Settings.Entities & CelesteNetClientSettings.SyncMode.Receive) == 0 ? Dummy<DataPlayerFrame.Entity>.EmptyArray : frame.Followers);
                ghost.UpdateHolding((Settings.Entities & CelesteNetClientSettings.SyncMode.Receive) == 0 ? null : frame.Holding);
            });
        }

        public void Handle(CelesteNetConnection con, DataAudioPlay audio) {
            if ((Settings.Sounds & CelesteNetClientSettings.SyncMode.Receive) == 0 || !(Engine.Scene is Level level) || level.Paused)
                return;

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

        public void Handle(CelesteNetConnection con, DataDashTrail trail) {
            if (!(Engine.Scene is Level level) || level.Paused)
                return;

            Ghost ghost = null;

            if (trail.Player != null) {
                Session session = Session;
                if (!Client.Data.TryGetBoundRef(trail.Player, out DataPlayerState state) ||
                    session == null ||
                    state.SID != session.Area.SID ||
                    state.Mode != session.Area.Mode ||
                    state.Level != session.Level ||
                    !Ghosts.TryGetValue(trail.Player.ID, out ghost))
                    return;
            }

            RunOnMainThread(() => {
                if (trail.Server) {
                    TrailManager.Add(
                        trail.Position,
                        trail.Sprite?.ToImage(),
                        ghost?.Hair,
                        trail.Scale,
                        trail.Color,
                        trail.Depth,
                        trail.Duration,
                        trail.FrozenUpdate,
                        trail.UseRawDeltaTime
                    );

                } else {
                    TrailManager.Add(
                        trail.Position,
                        ghost.Sprite,
                        ghost.Hair,
                        trail.Scale,
                        trail.Color,
                        ghost.Depth + 1,
                        1f,
                        false,
                        false
                    );
                }
            });
        }

        public void Handle(CelesteNetConnection con, DataMoveTo target) {
            Session session = Session;

            RunOnMainThread(() => {
                if (SaveData.Instance == null)
                    SaveData.InitializeDebugMode();
            }, true);

            AreaData area = AreaDataExt.Get(target.SID);

            if (area == null) {
                if (target.Force || string.IsNullOrEmpty(target.SID)) {
                    RunOnMainThread(() => {
                        OnExitLevel(null, null, LevelExit.Mode.SaveAndQuit, null, null);

                        string message = Dialog.Get("postcard_levelgone");
                        if (string.IsNullOrEmpty(target.SID))
                            message = Dialog.Get("postcard_celestenetclient_backtomenu");

                        message = message.Replace("((player))", SaveData.Instance.Name);
                        message = message.Replace("((sid))", target.SID);

                        LevelEnterExt.ErrorMessage = message;
                        LevelEnter.Go(new Session(new AreaKey(1).SetSID("")), false);
                    });
                }
                return;
            }

            if (session == null || session.Area.SID != target.SID || session.Area.Mode != target.Mode) {
                if (session != null)
                    UserIO.SaveHandler(true, true);

                session = new Session(area.ToKey(target.Mode));

            } else if (session != null) {
                // Best™ way to clone the session.
                XmlSerializer serializer = new XmlSerializer(typeof(Session));
                using (MemoryStream ms = new MemoryStream()) {
                    serializer.Serialize(ms, session);
                    ms.Seek(0, SeekOrigin.Begin);
                    session = (Session) serializer.Deserialize(ms);
                }
            }

            if (!string.IsNullOrEmpty(target.Level) && session.MapData.Get(target.Level) != null) {
                session.Level = target.Level;
                session.FirstLevel = false;
            }

            if (target.Session != null && target.Session.InSession) {
                DataSession data = target.Session;
                session.Audio = data.Audio.ToState();
                session.RespawnPoint = data.RespawnPoint;
                session.Inventory = data.Inventory;
                session.Flags = data.Flags;
                session.LevelFlags = data.LevelFlags;
                session.Strawberries = data.Strawberries;
                session.DoNotLoad = data.DoNotLoad;
                session.Keys = data.Keys;
                session.Counters = data.Counters;
                session.FurthestSeenLevel = data.FurthestSeenLevel;
                session.StartCheckpoint = data.StartCheckpoint;
                session.ColorGrade = data.ColorGrade;
                session.SummitGems = data.SummitGems;
                session.FirstLevel = data.FirstLevel;
                session.Cassette = data.Cassette;
                session.HeartGem = data.HeartGem;
                session.Dreaming = data.Dreaming;
                session.GrabbedGolden = data.GrabbedGolden;
                session.HitCheckpoint = data.HitCheckpoint;
                session.LightingAlphaAdd = data.LightingAlphaAdd;
                session.BloomBaseAdd = data.BloomBaseAdd;
                session.DarkRoomAlpha = data.DarkRoomAlpha;
                session.Time = data.Time;
                session.CoreMode = data.CoreMode;
            }

            if (target.Position != null) {
                Vector2? respawnPoint = session.RespawnPoint;
                session.RespawnPoint = target.Position;
                Action<Level> onBegin = null;
                onBegin = lvl => {
                    if (onBegin == null)
                        return;
                    QueuedTaskHelper.Do(target, () => {
                        if (lvl.Session.RespawnPoint == target.Position)
                            lvl.Session.RespawnPoint = respawnPoint ?? lvl.Session.RespawnPoint;
                    });
                    area.OnLevelBegin -= onBegin;
                    onBegin = null;
                };
                area.OnLevelBegin += onBegin;
            }

            session.StartedFromBeginning = false;

            RunOnMainThread(() => LevelEnter.Go(session, false));
        }

        public void Handle(CelesteNetConnection con, DataPlayerGrabPlayer grab) {
            Player player = Player;
            if (!(Engine.Scene is Level level) || level.Paused || player == null || !Settings.Interactions)
                goto Release;

            if (grab.Grabbing.ID == Client.PlayerInfo.ID) {
                if (!Ghosts.TryGetValue(grab.Player.ID, out Ghost ghost)) {
                    if (grab.Force == null)
                        goto Release;
                    return;
                }

                if (ghost.Holdable.IsHeld || !ghost.Holdable.ShouldHaveGravity)
                    goto Release;

                if (GrabbedBy != null && grab.Player.ID != GrabbedBy.PlayerInfo.ID)
                    goto Release;

                RunOnMainThread(() => {
                    GrabbedBy = ghost;
                    GrabTimeout = 0f;

                    player.Position = grab.Position;

                    if (grab.Force != null) {
                        player.ForceCameraUpdate = false;
                        player.StateMachine.State = Player.StNormal;
                        GrabLastSpeed = player.Speed = grab.Force.Value.SafeNormalize() * 300f + Vector2.UnitY * -70f;
                    } else {
                        player.ForceCameraUpdate = true;
                        player.StateMachine.State = Player.StFrozen;
                        GrabLastSpeed = player.Speed = Vector2.Zero;
                        player.Hair.AfterUpdate(); // TODO: Replace with node offset update instead.
                    }
                });

            } else if (player.Holding?.Entity is Ghost ghost && ghost.PlayerInfo.ID == grab.Grabbing.ID && grab.Force != null) {
                RunOnMainThread(() => {
                    ghost.Collidable = false;
                    player.Drop();
                    ghost.Collidable = true;
                });
            }

            return;

            Release:
            Client?.Send(new DataPlayerGrabPlayer {
                UpdateID = FrameNextID++,

                Player = Client.PlayerInfo,
                Grabbing = Client.PlayerInfo,

                Force = new Vector2(0, 0)
            });
        }

        #endregion

        #region Request Handlers

        public void Handle(CelesteNetConnection con, DataSessionRequest request) {
            Session session = Session;

            if (session == null) {
                Client?.Send(new DataSession {
                    RequestID = request.ID,
                    InSession = false
                });

            } else {
                Client?.Send(new DataSession {
                    RequestID = request.ID,
                    InSession = true,

                    Audio = new DataPartAudioState(session.Audio),
                    RespawnPoint = session.RespawnPoint,
                    Inventory = session.Inventory,
                    Flags = new HashSet<string>(session.Flags ?? new HashSet<string>()),
                    LevelFlags = new HashSet<string>(session.LevelFlags ?? new HashSet<string>()),
                    Strawberries = new HashSet<EntityID>(session.Strawberries ?? new HashSet<EntityID>()),
                    DoNotLoad = new HashSet<EntityID>(session.DoNotLoad ?? new HashSet<EntityID>()),
                    Keys = new HashSet<EntityID>(session.Keys ?? new HashSet<EntityID>()),
                    Counters = new List<Session.Counter>(Session.Counters ?? new List<Session.Counter>()),
                    FurthestSeenLevel = session.FurthestSeenLevel,
                    StartCheckpoint = session.StartCheckpoint,
                    ColorGrade = session.ColorGrade,
                    SummitGems = session.SummitGems,
                    FirstLevel = session.FirstLevel,
                    Cassette = session.Cassette,
                    HeartGem = session.HeartGem,
                    Dreaming = session.Dreaming,
                    GrabbedGolden = session.GrabbedGolden,
                    HitCheckpoint = session.HitCheckpoint,
                    LightingAlphaAdd = session.LightingAlphaAdd,
                    BloomBaseAdd = session.BloomBaseAdd,
                    DarkRoomAlpha = session.DarkRoomAlpha,
                    Time = session.Time,
                    CoreMode = session.CoreMode
                });
            }
        }

        #endregion

        public void UpdateIdleTag(Entity target, ref GhostEmote idleTag, bool idle) {
            if (!(Engine.Scene is Level level)) {
                idle = false;
                level = null;
            }

            if (target == null || target.Scene != level)
                idle = false;

            if (idle && idleTag == null) {
                level.Add(idleTag = new GhostEmote(target, "i:hover/idle") {
                    Position = target.Position,
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

            bool ready = Client != null && Client.IsReady && Client.PlayerInfo != null;
            if (!(Engine.Scene is Level level) || !ready) {
                GrabbedBy = null;

                if (ready && Engine.Scene is MapEditor) {
                    Player = null;
                    Session = null;
                    WasIdle = false;
                    AreaKey area = (AreaKey) f_MapEditor_area.GetValue(null);

                    if (MapEditorArea == null || MapEditorArea.Value.SID != area.SID || MapEditorArea.Value.Mode != area.Mode) {
                        MapEditorArea = area;
                        SendState();
                    }
                }

                if (Player != null && MapEditorArea == null) {
                    Player = null;
                    Session = null;
                    WasIdle = false;
                    SendState();
                }
                return;
            }

            bool grabReleased = false;
            grabReleased |= GrabbedBy != null && (GrabTimeout += Engine.RawDeltaTime) >= GrabTimeoutMax;
            grabReleased |= GrabbedBy != null && GrabbedBy.Scene != level;

            if (grabReleased)
                GrabbedBy = null;

            MapEditorArea = null;

            if (level.FrozenOrPaused || level.Overlay is PauseUpdateOverlay) {
                level.Particles.Update();
                level.ParticlesFG.Update();
                level.ParticlesBG.Update();
                if (TrailManager == null || TrailManager.Scene != level)
                    TrailManager = level.Tracker.GetEntity<TrailManager>();
                if (TrailManager != null)
                    foreach (TrailManager.Snapshot snapshot in TrailManager.GetSnapshots())
                        snapshot?.Update();
            }

            bool sendState = StateUpdated;
            StateUpdated = false;

            if (Player == null || Player.Scene != level) {
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

            if (grabReleased) {
                Player.ForceCameraUpdate = false;
                Player.StateMachine.State = Player.StNormal;
            }

            if (GrabbedBy != null)
                Player.Speed = GrabLastSpeed;
            if (Player.Holding?.Entity is Ghost ghost && ghost.Scene != level)
                Player.Holding = null;

            if (PlayerNameTag == null || PlayerNameTag.Tracking != Player || PlayerNameTag.Scene != level) {
                PlayerNameTag?.RemoveSelf();
                level.Add(PlayerNameTag = new GhostNameTag(Player, Client.PlayerInfo.DisplayName));
            }

            SendFrame();
        }

        #region Hooks

        public void OnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level level, Player.IntroTypes playerIntro, bool isFromLoader = false) {
            orig(level, playerIntro, isFromLoader);

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
            if (self.Entity is Ghost ghost && ghost.HairColors != null && 0 <= index && index < ghost.HairColors.Length)
                return ghost.HairColors[index] * ghost.Alpha;
            return orig(self, index);
        }

        private MTexture OnGetHairTexture(On.Celeste.PlayerHair.orig_GetHairTexture orig, PlayerHair self, int index) {
            if (self.Entity is Ghost ghost && ghost.HairTextures != null && 0 <= index && index < ghost.HairTextures.Length && GFX.Game.Textures.TryGetValue(ghost.HairTextures[index], out MTexture tex))
                return tex;
            return orig(self, index);
        }

        private EventInstance OnPlayerPlayAudio(On.Celeste.Player.orig_Play orig, Player self, string sound, string param, float value) {
            SendAudioPlay(self.Center, sound, param, value);
            return orig(self, sound, param, value);
        }

        private TrailManager.Snapshot OnDashTrailAdd(
            On.Celeste.TrailManager.orig_Add_Vector2_Image_PlayerHair_Vector2_Color_int_float_bool_bool orig,
            Vector2 position, Image sprite, PlayerHair hair, Vector2 scale, Color color, int depth, float duration, bool frozenUpdate, bool useRawDeltaTime
        ) {
            if (hair?.Entity is Player)
                SendDashTrail(position, sprite, hair, scale, color, depth, duration, frozenUpdate, useRawDeltaTime);
            return orig(position, sprite, hair, scale, color, depth, duration, frozenUpdate, useRawDeltaTime);
        }

        private void OnPlayerSpriteCtor(On.Celeste.PlayerSprite.orig_ctor orig, PlayerSprite self, PlayerSpriteMode mode) {
            orig(self, mode & (PlayerSpriteMode) ~(1 << 31));
        }

        #endregion


        #region Send

        public void SendState() {
            try {
                Client?.SendAndHandle(new DataPlayerState {
                    Player = Client.PlayerInfo,
                    SID = Session?.Area.GetSID() ?? MapEditorArea?.SID ?? "",
                    Mode = Session?.Area.Mode ?? MapEditorArea?.Mode ?? AreaMode.Normal,
                    Level = Session?.Level ?? (MapEditorArea != null ? LevelDebugMap : ""),
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

            DataPlayerFrame.Entity[] followers;
            DataPlayerFrame.Entity holding = null;

            if ((Settings.Entities & CelesteNetClientSettings.SyncMode.Send) == 0) {
                followers = Dummy<DataPlayerFrame.Entity>.EmptyArray;

            } else {
                Leader leader = Player.Get<Leader>();
                followers = new DataPlayerFrame.Entity[leader.Followers.Count];
                for (int i = 0; i < followers.Length; i++) {
                    Follower f = leader.Followers[i];
                    Sprite s = f.Entity.Get<Sprite>();
                    if (s == null) {
                        followers[i] = new DataPlayerFrame.Entity {
                            Scale = Vector2.One,
                            Color = Color.White,
                            Depth = -1000000,
                            SpriteRate = 1f,
                            SpriteJustify = null,
                            SpriteID = "",
                            CurrentAnimationID = "idle",
                            CurrentAnimationFrame = 0
                        };
                        continue;
                    }

                    followers[i] = new DataPlayerFrame.Entity {
                        Scale = s.Scale,
                        Color = s.Color,
                        Depth = f.Entity.Depth,
                        SpriteRate = s.Rate,
                        SpriteJustify = s.Justify,
                        SpriteID = s.GetID(),
                        CurrentAnimationID = s.CurrentAnimationID,
                        CurrentAnimationFrame = s.CurrentAnimationFrame
                    };
                }

                Holdable holdable = Player.Holding;
                if (holdable != null) {
                    Sprite s = holdable.Entity.Get<Sprite>();
                    if (s?.GetType() == typeof(Sprite)) {
                        holding = new DataPlayerFrame.Entity {
                            Position = holdable.Entity.Position,
                            Scale = s.Scale,
                            Color = s.Color,
                            Depth = holdable.Entity.Depth,
                            SpriteRate = s.Rate,
                            SpriteJustify = s.Justify,
                            SpriteID = s.GetID(),
                            CurrentAnimationID = s.CurrentAnimationID,
                            CurrentAnimationFrame = s.CurrentAnimationFrame
                        };
                    }
                }
            }

            try {
                Client?.Send(new DataPlayerFrame {
                    UpdateID = FrameNextID++,

                    Player = Client.PlayerInfo,

                    Position = Player.Position,
                    Speed = Player.Speed,
                    Scale = Player.Sprite.Scale,
                    Color = Player.Sprite.Color,
                    Facing = Player.Facing,
                    Depth = Player.Depth,

                    SpriteMode = Player.Sprite.Mode,
                    CurrentAnimationID = Player.Sprite.CurrentAnimationID,
                    CurrentAnimationFrame = Player.Sprite.CurrentAnimationFrame,

                    HairColor = Player.Hair.Color,
                    HairSimulateMotion = Player.Hair.SimulateMotion,

                    HairCount = (byte) hairCount,
                    HairColors = hairColors,
                    HairTextures = hairTextures,

                    Followers = followers,

                    Holding = holding,

                    // TODO: Get rid of this, sync particles separately!
                    DashWasB = Player.StateMachine.State == Player.StDash ? Player.GetWasDashB() : (bool?) null,
                    DashDir = Player.DashDir,

                    Dead = Player.Dead
                });
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendFrame:\n{e}");
                Context.Dispose();
            }
        }

        public void SendAudioPlay(Vector2 pos, string sound, string param = null, float value = 0f) {
            if ((Settings.Sounds & CelesteNetClientSettings.SyncMode.Send) == 0)
                return;

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

        public void SendDashTrail(Vector2 position, Image sprite, PlayerHair hair, Vector2 scale, Color color, int depth, float duration, bool frozenUpdate, bool useRawDeltaTime) {
            try {
                Client?.Send(new DataDashTrail {
                    Player = Client.PlayerInfo,
                    Position = position,
                    Sprite = new DataPartImage(sprite),
                    Scale = scale,
                    Color = color,
                    Depth = depth,
                    Duration = duration,
                    FrozenUpdate = frozenUpdate,
                    UseRawDeltaTime = useRawDeltaTime
                });
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendDashTrail:\n{e}");
                Context.Dispose();
            }
        }

        #endregion

    }
}
