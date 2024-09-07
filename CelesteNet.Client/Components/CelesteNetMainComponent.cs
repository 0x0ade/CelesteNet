using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Celeste.Editor;
using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Core;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetMainComponent : CelesteNetGameComponent {

        public const string LevelDebugMap = ":celestenet_debugmap:";

        private static readonly FieldInfo f_MapEditor_area =
            typeof(MapEditor)
            .GetField("area", BindingFlags.NonPublic | BindingFlags.Static);

        private Player Player;
        private Entity PlayerBody;
        private TrailManager TrailManager;
        private Session Session;
        private AreaKey? MapEditorArea;
        private bool WasIdle;
        private bool WasInteractive;
        private int SentHairLength = 0;
        private static int? LastDashes;

        public HashSet<string> ForceIdle = new();
        public bool StateUpdated;

        public GhostNameTag PlayerNameTag;
        public GhostEmote PlayerIdleTag;
        public ConcurrentDictionary<uint, Ghost> Ghosts = new();
        public ConcurrentDictionary<uint, DataPlayerFrame> LastFrames = new();
        public ConcurrentDictionary<uint, DataPlayerDashExt> LastDashExt = new();

        public ConcurrentDictionary<string, int> SpriteAnimationIDs = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<PlayerSpriteMode> UnsupportedSpriteModes = new();

        public Ghost GrabbedBy;
        public Vector2 GrabLastSpeed;
        public bool IsGrabbed = false;
        public float GrabCooldown = 0f;
        public const float GrabCooldownMax = 0.3f;
        public float GrabTimeout = 0f;
        public const float GrabTimeoutMax = 0.3f;

        private Vector2? NextRespawnPosition;

        private ILHook ILHookTransitionRoutine;

        public CelesteNetMainComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10200;
            Visible = false;
        }

        public override void Initialize() {
            base.Initialize();

            MainThreadHelper.Schedule(() => {
                // modern monomod does detourcontexts differently
                using (new DetourConfigContext(new DetourConfig(
                    "CelesteNetMain",
                    int.MinValue  // this simulates before: "*"
                )).Use()) {
                    On.Monocle.Scene.SetActualDepth += OnSetActualDepth;
                    On.Celeste.Level.LoadLevel += OnLoadLevel;
                    Everest.Events.Level.OnExit += OnExitLevel;
                    On.Celeste.Level.LoadNewPlayer += OnLoadNewPlayer;
                    On.Celeste.Player.Added += OnPlayerAdded;
                    On.Celeste.Player.Die += OnPlayerDie;
                    On.Celeste.Player.ResetSprite += OnPlayerResetSprite;
                    On.Celeste.Player.Play += OnPlayerPlayAudio;
                    On.Celeste.PlayerSprite.ctor += OnPlayerSpriteCtor;
                    On.Celeste.PlayerHair.GetHairColor += OnGetHairColor;
                    On.Celeste.PlayerHair.GetHairScale += OnGetHairScale;
                    On.Celeste.PlayerHair.GetHairTexture += OnGetHairTexture;
                    On.Celeste.TrailManager.Add_Vector2_Image_PlayerHair_Vector2_Color_int_float_bool_bool += OnDashTrailAdd;

                    MethodInfo transitionRoutine =
                        typeof(Level).GetNestedType("<TransitionRoutine>d__24", BindingFlags.NonPublic)
                        ?.GetMethod("MoveNext");
                    if (transitionRoutine != null)
                        ILHookTransitionRoutine = new(transitionRoutine, ILTransitionRoutine);
                }
            });
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            try {
                MainThreadHelper.Schedule(() => {
                    On.Monocle.Scene.SetActualDepth -= OnSetActualDepth;
                    On.Celeste.Level.LoadLevel -= OnLoadLevel;
                    Everest.Events.Level.OnExit -= OnExitLevel;
                    On.Celeste.Level.LoadNewPlayer -= OnLoadNewPlayer;
                    On.Celeste.Player.Added -= OnPlayerAdded;
                    On.Celeste.Player.ResetSprite -= OnPlayerResetSprite;
                    On.Celeste.Player.Play -= OnPlayerPlayAudio;
                    On.Celeste.PlayerSprite.ctor -= OnPlayerSpriteCtor;
                    On.Celeste.PlayerHair.GetHairColor -= OnGetHairColor;
                    On.Celeste.PlayerHair.GetHairScale -= OnGetHairScale;
                    On.Celeste.PlayerHair.GetHairTexture -= OnGetHairTexture;
                    On.Celeste.TrailManager.Add_Vector2_Image_PlayerHair_Vector2_Color_int_float_bool_bool -= OnDashTrailAdd;

                    ILHookTransitionRoutine?.Dispose();
                    ILHookTransitionRoutine = null;
                });
            } catch (ObjectDisposedException) {
                // It might already be too late to tell the main thread to do anything.
            }

            Cleanup();
        }

        public void Cleanup() {
            if (IsGrabbed && Player?.StateMachine.State == Player.StFrozen)
                Player.StateMachine.State = Player.StNormal;

            Player = null;
            PlayerBody = null;
            Session = null;
            WasIdle = false;
            WasInteractive = false;
            LastDashes = null;

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

        public void Handle(CelesteNetConnection con, DataDisconnectReason reason) {
            CelesteNetClientModule.Instance.lastDisconnectReason = reason;
            // attempting to disconnect from within TCP recv thread here could cause game freeze
            // if StartThread was also trying to dispose client context at the same time.
            // So this will be handled within Client Context instead, based on lastDisconnectReason being set.
            //CelesteNetClientModule.Settings.Connected = false;
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo player) {
            if (Client?.Data == null)
                return;

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
                LastFrames.TryRemove(player.ID, out _);
                LastDashExt.TryRemove(player.ID, out _);
                Client.Data.FreeOrder<DataPlayerFrame>(player.ID);
                return;
            }
        }

        public void Handle(CelesteNetConnection con, DataChannelMove move) {
            if (Client?.Data == null)
                return;

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

                if (Settings.InGame.Interactions != state.Interactive && ghost == GrabbedBy)
                    SendReleaseMe();

                Session session = Session;
                if (session != null && (state.SID != session.Area.SID || state.Mode != session.Area.Mode || state.Level == LevelDebugMap)) {
                    ghost.RunOnUpdate(ghost => ghost.NameTag.Name = "");
                    Ghosts.TryRemove(id, out _);
                    return;
                }

                UpdateIdleTag(ghost, ref ghost.IdleTag, state.Idle);
            }
        }

        public void Handle(CelesteNetConnection con, DataPlayerGraphics graphics) {
            if (UnsupportedSpriteModes.Contains(graphics.SpriteMode))
                graphics.SpriteMode = PlayerSpriteMode.Madeline;

            if (!Ghosts.TryGetValue(graphics.Player.ID, out Ghost ghost) || ghost?.Sprite?.Mode != graphics.SpriteMode) {
                RemoveGhost(graphics.Player);
                ghost = null;
            }

            Level level = PlayerBody?.Scene as Level;
            if (ghost == null && !IsGhostOutside(Session, level, graphics.Player, out _))
                ghost = CreateGhost(level, graphics.Player, graphics);

            if (ghost != null) {
                ghost.RunOnUpdate(ghost => {
                    ghost.UpdateGraphics(graphics);
                });
            }
        }

        public void Handle(CelesteNetConnection con, DataPlayerFrame frame) {
            if (Client?.Data == null)
                return;

            if (frame.HairColors.Length > Ghost.MaxHairLength)
                Array.Resize(ref frame.HairColors, Ghost.MaxHairLength);

            LastFrames[frame.Player.ID] = frame;

            Session session = Session;
            Level level = PlayerBody?.Scene as Level;
            bool outside = IsGhostOutside(session, level, frame.Player, out DataPlayerState state);

            if (!Ghosts.TryGetValue(frame.Player.ID, out Ghost ghost) ||
                ghost == null ||
                (ghost.Active && ghost.Scene != level) ||
                outside) {
                RemoveGhost(frame.Player);
                ghost = null;
            }

            if (level == null || outside)
                return;

            if (ghost == null) {
                if (!Client.Data.TryGetBoundRef<DataPlayerInfo, DataPlayerGraphics>(frame.Player, out DataPlayerGraphics graphics) || graphics == null)
                    return;
                ghost = CreateGhost(level, frame.Player, graphics);
            }

            ghost.RunOnUpdate(ghost => {
                if (string.IsNullOrEmpty(ghost.NameTag.Name))
                    return;
                ghost.NameTag.Name = frame.Player.DisplayName;
                UpdateIdleTag(ghost, ref ghost.IdleTag, state.Idle);
                ghost.UpdateGeneric(frame.Position, frame.Scale, frame.Color, frame.Facing, frame.Speed);
                ghost.UpdateAnimation(frame.CurrentAnimationID, frame.CurrentAnimationFrame);
                ghost.UpdateHair(frame.Facing, frame.HairColors, frame.HairTexture0, frame.HairSimulateMotion && !state.Idle);
                ghost.UpdateDash(frame.DashWasB, frame.DashDir); // TODO: Get rid of this, sync particles separately!
                ghost.UpdateDead(frame.Dead && state.Level == session?.Level);
                ghost.UpdateFollowers((Settings.InGame.Entities & CelesteNetClientSettings.SyncMode.Receive) == 0 ? Dummy<DataPlayerFrame.Entity>.EmptyArray : frame.Followers);
                ghost.UpdateHolding((Settings.InGame.Entities & CelesteNetClientSettings.SyncMode.Receive) == 0 ? null : frame.Holding);
                ghost.Interactive = state.Interactive;
            });
        }

        public void Handle(CelesteNetConnection con, DataAudioPlay audio) {
            if (Client?.Data == null)
                return;

            if ((Settings.InGame.Sounds & CelesteNetClientSettings.SyncMode.Receive) == 0 || Engine.Scene is not Level level || level.Paused)
                return;

            Ghost ghost = null;

            if (audio.Player != null) {
                Session session = Session;
                if (!Client.Data.TryGetBoundRef(audio.Player, out DataPlayerState state) ||
                    session == null ||
                    state.SID != session.Area.SID ||
                    state.Mode != session.Area.Mode ||
                    state.Level != session.Level)
                    return;

                if (!Ghosts.TryGetValue(audio.Player.ID, out ghost))
                    ghost = null;
            }

            if (audio.Position == null) {
                PlayAudio(ghost, audio.Sound, null, audio.Param, audio.Value);
                return;
            }

            PlayAudio(ghost, audio.Sound, audio.Position.Value, audio.Param, audio.Value);
        }

        public void Handle(CelesteNetConnection con, DataDashTrail trail) {
            if (Client?.Data == null || Engine.Scene is not Level level || level.Paused)
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
            RunOnMainThread(() => {
                Session session = Session;

                if (SaveData.Instance == null)
                    SaveData.InitializeDebugMode();

                AreaData area = AreaData.Get(target.SID);

                if (area == null) {
                    if (target.Force || string.IsNullOrEmpty(target.SID)) {
                        RunOnMainThread(() => {
                            OnExitLevel(null, null, LevelExit.Mode.SaveAndQuit, null, null);

                            string message = Dialog.Get("postcard_levelgone");
                            if (string.IsNullOrEmpty(target.SID))
                                message = Dialog.Get("postcard_celestenetclient_backtomenu");

                            message = message.Replace("((player))", SaveData.Instance.Name);
                            message = message.Replace("((sid))", target.SID);

                            LevelEnter.ErrorMessage = message;
                            LevelEnter.Go(new(new AreaKey(1).SetSID("")), false);
                        });
                    }
                    return;
                }

                if (session == null || session.Area.SID != target.SID || session.Area.Mode != target.Mode) {
                    if (session != null)
                        UserIO.SaveHandler(true, true);

                    session = new(area.ToKey(target.Mode));

                } else if (session != null) {
                    // Best™ way to clone the session.
                    XmlSerializer serializer = new(typeof(Session));
                    using MemoryStream ms = new();
                    serializer.Serialize(ms, session);
                    ms.Seek(0, SeekOrigin.Begin);
                    session = (Session) serializer.Deserialize(ms);
                }

                if (!string.IsNullOrEmpty(target.Level) && session?.MapData.Get(target.Level) != null) {
                    session.Level = target.Level;
                    session.FirstLevel = false;
                }

                if (session != null) {
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
                    session.StartedFromBeginning = false;
                }

                NextRespawnPosition = target.Position ?? session?.RespawnPoint;

                LevelEnter.Go(session, true);
            });
        }

        public void Handle(CelesteNetConnection con, DataPlayerGrabPlayer grab) {
            if (Client?.Data == null)
                return;

            Player player = Player;
            if (player != null && !Settings.InGame.Interactions && (grab.Player.ID == Client.PlayerInfo.ID || grab.Grabbing.ID == Client.PlayerInfo.ID))
                goto Release;

            if (Engine.Scene is not Level level || level.Paused || player == null || !Settings.InGame.Interactions)
                return;

            if (grab.Player.ID != Client.PlayerInfo.ID && grab.Grabbing.ID == Client.PlayerInfo.ID) {
                if (GrabCooldown > 0f) {
                    GrabCooldown = GrabCooldownMax;
                    goto Release;
                }

                if (!Ghosts.TryGetValue(grab.Player.ID, out Ghost ghost)) {
                    if (grab.Force == null)
                        goto Release;
                    return;
                }

                if ((ghost.Holdable.IsHeld || !ghost.Holdable.ShouldHaveGravity) && grab.GrabStrength < ghost.GrabStrength)
                    goto Release;

                if (GrabbedBy != null && grab.Player.ID != GrabbedBy.PlayerInfo.ID)
                    goto Release;

                if ((ghost.Position - player.Position).LengthSquared() > 128f * 128f)
                    goto Release;

                if ((grab.Position - player.Position).LengthSquared() > 128f * 128f)
                    goto Release;

                RunOnMainThread(() => {
                    GrabTimeout = 0f;

                    player.Position = Calc.Round(grab.Position);

                    if (grab.Force != null) {
                        GrabbedBy = null;
                        IsGrabbed = false;
                        player.ForceCameraUpdate = false;
                        player.StateMachine.State = Player.StNormal;
                        GrabLastSpeed = player.Speed = grab.Force.Value.SafeNormalize() * 300f + Vector2.UnitY * -70f;

                    } else {
                        GrabbedBy = ghost;
                        IsGrabbed = true;
                        player.ForceCameraUpdate = true;
                        player.StateMachine.State = Player.StFrozen;
                        GrabLastSpeed = player.Speed = Vector2.Zero;
                        player.Hair.AfterUpdate(); // TODO: Replace with node offset update instead.
                        if (player.Scene == level) {
                            try {
                                level.EnforceBounds(player);
                            } catch (Exception e) {
                                Logger.Log(LogLevel.CRI, "client-main", $"Error on EnforceBounds on hold:\n{e}");
                            }
                        }
                    }
                });

            } else if (player.Holding?.Entity is Ghost ghost && ghost.PlayerInfo.ID == grab.Grabbing.ID && grab.Force != null) {
                RunOnMainThread(() => {
                    if (player.Holding?.Entity == ghost && ghost.Scene == player.Scene && player.Scene != null) {
                        ghost.Collidable = false;
                        player.Drop();
                        ghost.GrabCooldown = Ghost.GrabCooldownMax;
                    }
                });
            }

            return;

            Release:
            SendReleaseMe();
        }
        public void Handle(CelesteNetConnection con, DataPlayerDashExt frame) {
            if (Client?.Data == null)
                return;
            LastDashExt[frame.Player.ID] = frame;

            Session session = Session;
            Level level = PlayerBody?.Scene as Level;
            bool outside = IsGhostOutside(session, level, frame.Player, out DataPlayerState state);
            if (!Ghosts.TryGetValue(frame.Player.ID, out Ghost ghost) || ghost == null || (ghost.Active && ghost.Scene != level) || outside) {
                RemoveGhost(frame.Player);
                return;
            }
            if (level == null || outside)
                return;
            ghost.RunOnUpdate(ghost => {
                if (string.IsNullOrEmpty(ghost.NameTag.Name))
                    return;
                ghost.Dashes = frame.Dashes;
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

                    Audio = new(session.Audio),
                    RespawnPoint = session.RespawnPoint,
                    Inventory = session.Inventory,
                    Flags = new(session.Flags ?? new HashSet<string>()),
                    LevelFlags = new(session.LevelFlags ?? new HashSet<string>()),
                    Strawberries = new(session.Strawberries ?? new HashSet<EntityID>()),
                    DoNotLoad = new(session.DoNotLoad ?? new HashSet<EntityID>()),
                    Keys = new(session.Keys ?? new HashSet<EntityID>()),
                    Counters = new(Session.Counters ?? new List<Session.Counter>()),
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

        protected bool IsGhostOutside(Session ses, Level level, DataPlayerInfo player, out DataPlayerState state) {
            state = null;
            return
                level == null ||
                ses == null ||
                Client?.Data?.TryGetBoundRef(player, out state) != true ||
                state.SID != ses.Area.SID ||
                state.Mode != ses.Area.Mode ||
                state.Level == LevelDebugMap;
        }

        protected Ghost CreateGhost(Level level, DataPlayerInfo player, DataPlayerGraphics graphics) {
            Ghost ghost;
            lock (Ghosts)
                if (!Ghosts.TryGetValue(player.ID, out ghost) || ghost == null) {
                    ghost = Ghosts[player.ID] = new(Context, player, graphics.SpriteMode);
                    ghost.Active = false;
                    ghost.NameTag.Name = player.DisplayName;
                    if (ghost.Sprite.Mode != graphics.SpriteMode)
                        UnsupportedSpriteModes.Add(graphics.SpriteMode);
                    RunOnMainThread(() => {
                        level.Add(ghost);
                        level.OnEndOfFrame += () => ghost.Active = true;
                        ghost.UpdateGraphics(graphics);
                    });
                    ghost.UpdateGraphics(graphics);
                    if (LastDashExt.TryGetValue(player.ID, out var lastDashExt)) {
                        ghost.Dashes = lastDashExt.Dashes;
                    }
                    LastDashes = null; // There is a new ghost!... Refresh Dashes to let others sync with we
                }
            return ghost;
        }

        protected void RemoveGhost(DataPlayerInfo info) {
            Ghosts.TryRemove(info.ID, out Ghost ghost);
            ghost?.RunOnUpdate(g => g.NameTag.Name = "");
        }

        public void UpdateIdleTag(Entity target, ref GhostEmote idleTag, bool idle) {
            if (Engine.Scene is not Level level) {
                idle = false;
                level = null;
            }

            if (target == null || target.Scene != level)
                idle = false;

            if (idle && idleTag == null) {
                level.Add(idleTag = new(target, "i:hover/idle") {
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

        public EventInstance PlayAudio(Ghost ghost, string sound, Vector2? at, string param = null, float value = 0f) {
            if ((Settings.InGame.Sounds & CelesteNetClientSettings.SyncMode.Receive) == 0)
                return null;

            EventDescription desc = Audio.GetEventDescription(sound);
            if (desc == null)
                return null;
            
            desc.is3D(out bool is3D);

            if (ghost != null) {
                if (ghost.Scene is not Level level)
                    return null;

                // Note: dying outside of room = dying outside of cam range!
                if (!level.IsInCamera(ghost.Position, is3D ? 128f : 64f))
                    return null;
            }

            desc.createInstance(out EventInstance ev);
            if (ev == null)
                return null;

            if (is3D && at != null)
                Audio.Position(ev, at.Value);

            ev.setVolume(Settings.InGame.SoundVolume / 10f);

            if (!string.IsNullOrEmpty(param))
                ev.setParameterValue(param, value);

            ev.start();
            ev.release();

            return ev;
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            bool ready = Client != null && Client.IsReady && Client.PlayerInfo != null;
            if (Engine.Scene is not Level level || !ready) {
                GrabbedBy = null;

                if (ready && Engine.Scene is MapEditor) {
                    Player = null;
                    PlayerBody = null;
                    Session = null;
                    WasIdle = false;
                    WasInteractive = false;
                    AreaKey area = (AreaKey) f_MapEditor_area.GetValue(null);

                    if (MapEditorArea == null || MapEditorArea.Value.SID != area.SID || MapEditorArea.Value.Mode != area.Mode) {
                        MapEditorArea = area;
                        SendState();
                    }
                }

                if (Player != null && MapEditorArea == null) {
                    Player = null;
                    PlayerBody = null;
                    Session = null;
                    WasIdle = false;
                    WasInteractive = false;
                    SendState();
                }
                return;
            }

            bool grabReleased = false;
            grabReleased |= IsGrabbed && (GrabTimeout += Engine.RawDeltaTime) >= GrabTimeoutMax;
            grabReleased |= GrabbedBy != null && GrabbedBy.Scene != level;

            if (grabReleased) {
                GrabbedBy = null;
                IsGrabbed = false;
            }

            if (!IsGrabbed) {
                GrabTimeout = 0f;
            }

            GrabCooldown -= Engine.RawDeltaTime;
            if (GrabCooldown < 0f)
                GrabCooldown = 0f;

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

            if (Player == null || Player.Scene != level) {
                Player = level.Tracker.GetEntity<Player>();
                if (Player != null) {
                    PlayerBody = Player;
                    Session = level.Session;
                    WasIdle = false;
                    WasInteractive = false;
                    StateUpdated |= true;
                    SendGraphics();
                }
            }

            if (Player != null && Player.Sprite != null && SentHairLength != Player.Sprite.HairCount)
                SendGraphics();

            bool idle = level.FrozenOrPaused || level.Overlay != null;
            if (WasIdle != idle) {
                WasIdle = idle;
                StateUpdated |= true;
            }

            if (WasInteractive != Settings.InGame.Interactions) {
                WasInteractive = Settings.InGame.Interactions;
                StateUpdated |= true;
            }

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

            if (IsGrabbed && !idle && Player.StateMachine.State == Player.StFrozen) {
                if (Input.Jump.Pressed) {
                    Player.StateMachine.State = Player.StNormal;
                    Player.Jump(true, true);
                    GrabCooldown = GrabCooldownMax;
                    SendReleaseMe();
                }
            }

            if (PlayerNameTag == null || PlayerNameTag.Tracking != Player || PlayerNameTag.Scene != level) {
                PlayerNameTag?.RemoveSelf();
                level.Add(PlayerNameTag = new(Player, Client.PlayerInfo.DisplayName));
            }
            PlayerNameTag.Alpha = Settings.InGameHUD.ShowOwnName ? 1f : 0f;
        }

        public override void Tick() {
            if (StateUpdated)
                SendState();
            StateUpdated = false;

            if (Player != null)
                SendFrame();
        }

        #region Hooks

        public void OnSetActualDepth(On.Monocle.Scene.orig_SetActualDepth orig, Scene scene, Entity entity) {
            orig(scene, entity);

            if (Client != null && entity == Player)
                SendGraphics();
        }

        public void OnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level level, Player.IntroTypes playerIntro, bool isFromLoader = false) {
            orig(level, playerIntro, isFromLoader);

            Session = level.Session;
            WasIdle = false;
            WasInteractive = false;

            if (Client == null)
                return;

            Player = level.Tracker.GetEntity<Player>();
            PlayerBody = Player;

            SendState();
        }

        public void OnExitLevel(Level level, LevelExit exit, LevelExit.Mode mode, Session session, HiresSnow snow) {
            Session = null;
            WasIdle = false;
            WasInteractive = false;

            Cleanup();

            SendState();
        }

        private Player OnLoadNewPlayer(On.Celeste.Level.orig_LoadNewPlayer orig, Vector2 position, PlayerSpriteMode spriteMode) {
            Player player = orig(position, spriteMode);
            if (NextRespawnPosition != null) {
                player.Position = NextRespawnPosition.Value;
                NextRespawnPosition = null;
            }
            return player;
        }

        private void OnPlayerAdded(On.Celeste.Player.orig_Added orig, Player self, Scene scene) {
            orig(self, scene);

            Session = (scene as Level)?.Session;
            WasIdle = false;
            WasInteractive = false;
            Player = self;
            PlayerBody = self;

            SendState();
            SendGraphics();

            foreach (DataPlayerFrame frame in LastFrames.Values.ToArray())
                Handle(null, frame);
        }

        private PlayerDeadBody OnPlayerDie(On.Celeste.Player.orig_Die orig, Player self, Vector2 direction, bool evenIfInvincible, bool registerDeathInStats) {
            PlayerDeadBody body = orig(self, direction, evenIfInvincible, registerDeathInStats);
            PlayerBody = body ?? (Entity) self;
            return body;
        }

        private void OnPlayerResetSprite(On.Celeste.Player.orig_ResetSprite orig, Player self, PlayerSpriteMode mode) {
            orig(self, mode);
            SendGraphics();
        }

        private EventInstance OnPlayerPlayAudio(On.Celeste.Player.orig_Play orig, Player self, string sound, string param, float value) {
            SendAudioPlay(self.Center, sound, param, value);
            return orig(self, sound, param, value);
        }

        private void OnPlayerSpriteCtor(On.Celeste.PlayerSprite.orig_ctor orig, PlayerSprite self, PlayerSpriteMode mode) {
            orig(self, mode & (PlayerSpriteMode) ~(1 << 31));
        }

        private Color OnGetHairColor(On.Celeste.PlayerHair.orig_GetHairColor orig, PlayerHair self, int index) {
            if (self.Entity is Ghost ghost && ghost.HairColors != null && 0 <= index && index < ghost.HairColors.Length)
                return ghost.HairColors[index] * ghost.Alpha;
            return orig(self, index);
        }

        private Vector2 OnGetHairScale(On.Celeste.PlayerHair.orig_GetHairScale orig, PlayerHair self, int index) {
            if (self.Entity is Ghost ghost && ghost.PlayerGraphics.HairScales != null && 0 <= index && index < ghost.PlayerGraphics.HairScales.Length)
                return ghost.PlayerGraphics.HairScales[index] * new Vector2((int) ghost.Hair.Facing, 1);
            return orig(self, index);
        }

        private MTexture OnGetHairTexture(On.Celeste.PlayerHair.orig_GetHairTexture orig, PlayerHair self, int index) {
            if (self.Entity is Ghost ghost && ghost.PlayerGraphics.HairTextures != null && 0 <= index && index < ghost.PlayerGraphics.HairTextures.Length && GFX.Game.Textures.TryGetValue(ghost.PlayerGraphics.HairTextures[index], out MTexture tex))
                return tex;
            return orig(self, index);
        }

        private TrailManager.Snapshot OnDashTrailAdd(
            On.Celeste.TrailManager.orig_Add_Vector2_Image_PlayerHair_Vector2_Color_int_float_bool_bool orig,
            Vector2 position, Image sprite, PlayerHair hair, Vector2 scale, Color color, int depth, float duration, bool frozenUpdate, bool useRawDeltaTime
        ) {
            if (hair?.Entity is Player)
                SendDashTrail(position, sprite, hair, scale, color, depth, duration, frozenUpdate, useRawDeltaTime);
            return orig(position, sprite, hair, scale, color, depth, duration, frozenUpdate, useRawDeltaTime);
        }

        private void ILTransitionRoutine(ILContext il) {
            ILCursor c = new(il);

            if (c.TryGotoNext(i => i.MatchLdstr("Celeste"))) {
                c.Next.Operand = "";
            }

            if (c.TryGotoNext(i => i.MatchCallOrCallvirt<CoreModuleSettings>("get_DisableAntiSoftlock"))) {
                c.Index++;
                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Ldc_I4_0);
            }
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
                    Idle = ForceIdle.Count != 0 || (Player?.Scene is Level level && (level.FrozenOrPaused || level.Overlay != null)),
                    Interactive = Settings.InGame.Interactions
                });
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendState:\n{e}");
                Context.DisposeSafe();
            }
        }

        public void SendGraphics() {
            Player player = Player;
            if (player == null || player.Sprite == null || player.Hair == null)
                return;

            SpriteAnimationIDs.Clear();
            string[] animations = new string[player.Sprite.Animations.Count];
            int animI = 0;
            foreach (string animID in player.Sprite.Animations.Keys) {
                SpriteAnimationIDs.TryAdd(animID, animI);
                animations[animI] = animID;
                animI++;
            }

            int hairCount = Calc.Clamp(player.Sprite.HairCount, 0, Ghost.MaxHairLength);
            Vector2[] hairScales = new Vector2[hairCount];
            for (int i = 0; i < hairCount; i++)
                hairScales[i] = player.Hair.PublicGetHairScale(i) * new Vector2(((i == 0) ? (int) player.Hair.Facing : 1) / Math.Abs(player.Sprite.Scale.X), 1);
            string[] hairTextures = new string[hairCount];
            for (int i = 0; i < hairCount; i++)
                hairTextures[i] = player.Hair.GetHairTexture(i).AtlasPath;

            try {
                Client?.Send(new DataPlayerGraphics {
                    Player = Client.PlayerInfo,

                    Depth = player.Depth,
                    SpriteMode = player.Sprite.Mode,
                    SpriteRate = player.Sprite.Rate,
                    SpriteAnimations = animations,

                    HairCount = (byte) hairCount,
                    HairStepPerSegment = player.Hair.StepPerSegment,
                    HairStepInFacingPerSegment = player.Hair.StepInFacingPerSegment,
                    HairStepApproach = player.Hair.StepApproach,
                    HairStepYSinePerSegment = player.Hair.StepYSinePerSegment,
                    HairScales = hairScales,
                    HairTextures = hairTextures
                });
                SentHairLength = hairCount;
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendGraphics:\n{e}");
                Context.DisposeSafe();
            }
        }

        public void SendFrame() {
            Player player = Player;
            if (player == null || player.Sprite == null || player.Hair == null)
                return;

            DataPlayerFrame.Entity[] followers;
            DataPlayerFrame.Entity holding = null;

            if ((Settings.InGame.Entities & CelesteNetClientSettings.SyncMode.Send) == 0) {
                followers = Dummy<DataPlayerFrame.Entity>.EmptyArray;
            } else {
                Leader leader = player.Get<Leader>();
                followers = new DataPlayerFrame.Entity[leader.Followers.Count];
                for (int i = 0; i < followers.Length; i++) {
                    Entity f = leader.Followers[i]?.Entity;
                    Sprite s = f?.Get<Sprite>();
                    if (s == null) {
                        followers[i] = new() {
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

                    followers[i] = new() {
                        Scale = s.Scale,
                        Color = s.Color,
                        Depth = f.Depth,
                        SpriteRate = s.Rate,
                        SpriteJustify = s.Justify,
                        SpriteID = s.GetID(),
                        CurrentAnimationID = s.CurrentAnimationID,
                        CurrentAnimationFrame = s.CurrentAnimationFrame
                    };
                }

                Entity holdable = player.Holding?.Entity;
                if (holdable != null) {
                    Sprite s = holdable.Get<Sprite>();
                    if (s?.GetType() == typeof(Sprite)) {
                        holding = new() {
                            Position = holdable.Position,
                            Scale = s.Scale,
                            Color = s.Color,
                            Depth = holdable.Depth,
                            SpriteRate = s.Rate,
                            SpriteJustify = s.Justify,
                            SpriteID = s.GetID(),
                            CurrentAnimationID = s.CurrentAnimationID,
                            CurrentAnimationFrame = s.CurrentAnimationFrame
                        };
                    }
                }
            }

            if (!SpriteAnimationIDs.TryGetValue(player.Sprite.CurrentAnimationID, out int animID))
                animID = -1;

            try {
                int hairCount = Calc.Clamp(player.Sprite.HairCount, 0, Ghost.MaxHairLength);

                Client?.Send(new DataPlayerFrame {
                    Player = Client.PlayerInfo,

                    Position = player.Position,
                    Scale = player.Sprite.Scale,
                    Color = player.Sprite.Color,
                    Facing = player.Facing,
                    Speed = player.Speed,

                    CurrentAnimationID = animID,
                    CurrentAnimationFrame = player.Sprite.CurrentAnimationFrame,

                    HairColors = Enumerable.Range(0, hairCount).Select(i => player.Hair.GetHairColor(i)).ToArray(),
                    HairTexture0 = player.Hair.GetHairTexture(0).AtlasPath,
                    HairSimulateMotion = player.Hair.SimulateMotion,

                    Followers = followers,
                    Holding = holding,

                    // TODO: Get rid of this, sync particles separately!
                    DashWasB = player.StateMachine.State == Player.StDash ? player.GetWasDashB() : null,
                    DashDir = player.StateMachine.State == Player.StDash ? player.DashDir : null,

                    Dead = player.Dead
                });
                if (LastDashes != player.Dashes) {
                    LastDashes = player.Dashes;

                    Client?.Send(new DataPlayerDashExt {
                        Player = Client.PlayerInfo,

                        Dashes = player.Dashes
                    });
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendFrame:\n{e}");
                Context.DisposeSafe();
            }
        }

        public void SendAudioPlay(Vector2 pos, string sound, string param = null, float value = 0f) {
            if ((Settings.InGame.Sounds & CelesteNetClientSettings.SyncMode.Send) == 0)
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
                Context.DisposeSafe();
            }
        }

        public void SendDashTrail(Vector2 position, Image sprite, PlayerHair hair, Vector2 scale, Color color, int depth, float duration, bool frozenUpdate, bool useRawDeltaTime) {
            try {
                Client?.Send(new DataDashTrail {
                    Player = Client.PlayerInfo,
                    Position = position,
                    Sprite = new(sprite),
                    Scale = scale,
                    Color = color,
                    Depth = depth,
                    Duration = duration,
                    FrozenUpdate = frozenUpdate,
                    UseRawDeltaTime = useRawDeltaTime
                });
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendDashTrail:\n{e}");
                Context.DisposeSafe();
            }
        }

        public void SendReleaseMe() {
            try {
                Client?.Send(new DataPlayerGrabPlayer {
                    Player = Client.PlayerInfo,
                    Grabbing = Client.PlayerInfo,

                    Force = new(0, 0)
                });
            } catch (Exception e) {
                Logger.Log(LogLevel.INF, "client-main", $"Error in SendReleaseMe:\n{e}");
                Context.DisposeSafe();
            }
        }

        #endregion

    }
}
