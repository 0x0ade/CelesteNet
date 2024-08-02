using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Pico8;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.CelesteNet.Client.Components;
#nullable enable


internal class GhostFrame
{
    internal float Spr;
    internal float X;
    internal float Y;
    internal bool FlipX;
    internal bool FlipY;
    internal int Type;
    internal int DJump;
    internal readonly List<DataPicoFrame.HairNode> Hair = new();
}

public class CelesteNetPico8Component : CelesteNetGameComponent {
    
    public readonly ConcurrentDictionary<uint, PicoGhost> Ghosts = new();

    private readonly GhostFrame _queuedGhostFrame = new();
    private bool _alive;

    public CelesteNetPico8Component(CelesteNetClientContext context, Game game) : base(context, game) {}

    public override void Initialize() {
        base.Initialize();

        #pragma warning disable CA2012
        MainThreadHelper.Schedule(() =>
        {
            using (new DetourConfigContext(new DetourConfig(
                "CelesteNetPico8",
                int.MinValue
            )).Use())
            {
                On.Celeste.Pico8.Classic.player.ctor += OnPlayerCreate;
                On.Celeste.Pico8.Classic.player_spawn.ctor += OnPlayerSpawn;
                On.Celeste.Pico8.Classic.player.update += OnPlayerUpdate;
                On.Celeste.Pico8.Classic.player_spawn.update += OnPlayerSpawnUpdate;
                On.Celeste.Pico8.Classic.player.draw += OnPlayerDraw;
                On.Celeste.Pico8.Classic.kill_player += OnPlayerKill;
                On.Celeste.Pico8.Classic.player_hair.draw_hair += OnDrawHair;
                On.Celeste.Pico8.Emulator.Update += OnEmulatorUpdate;
                On.Celeste.Pico8.Emulator.End += OnEmulatorClose;
                On.Celeste.Pico8.Emulator.ResetScreen += OnEmulatorReset;
            }
        });

        CelesteNetPlayerListComponent.OnGetState += ModifyStateOfPicoPlayers;
        CelesteNetMainComponent.OnSendState += ModifyStateInPico;
        var client = Context?.Client;
        if (client != null) {
            client.Con.OnDisconnect += WipeGhostsOnDisconnect;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try {
            MainThreadHelper.Schedule(() => {
                On.Celeste.Pico8.Classic.player.ctor -= OnPlayerCreate;
                On.Celeste.Pico8.Classic.player_spawn.ctor -= OnPlayerSpawn;
                On.Celeste.Pico8.Classic.player.update -= OnPlayerUpdate;
                On.Celeste.Pico8.Classic.player_spawn.update -= OnPlayerSpawnUpdate;
                On.Celeste.Pico8.Classic.player.draw -= OnPlayerDraw;
                On.Celeste.Pico8.Classic.kill_player -= OnPlayerKill;
                On.Celeste.Pico8.Classic.player_hair.draw_hair -= OnDrawHair;
                On.Celeste.Pico8.Emulator.End -= OnEmulatorClose;
                On.Celeste.Pico8.Emulator.ResetScreen -= OnEmulatorReset;
                On.Celeste.Pico8.Emulator.Update -= OnEmulatorUpdate;
            });
        } catch (ObjectDisposedException) {
            // It might already be too late to tell the main thread to do anything.
        }

        CelesteNetPlayerListComponent.OnGetState -= ModifyStateOfPicoPlayers;
        CelesteNetMainComponent.OnSendState -= ModifyStateInPico;
        var client = Context?.Client;
        if (client != null) {
            client.Con.OnDisconnect -= WipeGhostsOnDisconnect;
        }

        #pragma warning restore CA2012
    }

    #region Hooks

    private void WipeGhostsOnDisconnect(CelesteNetConnection connection)
    {
        Ghosts.Clear();
    }

    private void ModifyStateInPico(DataPlayerState state)
    {
        uint? id = state.Player?.ID;
        if (id == null) return;
        if (!_inGame) return;

        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"Modifying state!");

        state.SID = $"PICO-8";
        state.Mode = AreaMode.Normal;
        state.Interactive = false;
        state.Idle = false;
        
        int index = LevelIndex;
        if (index == -1) return;

        state.Level = $"{(index + 1) * 100}M";
    }

    private void ModifyStateOfPicoPlayers(CelesteNetPlayerListComponent.BlobPlayer player, DataPlayerState state)
    {
        if (state.SID != "PICO-8") return;
        
        player.Location.Icon = "menu/pico8";
        player.Location.Side = "";
        player.Location.TitleColor = new Color(0xff, 0xf1, 0xe8); // PICO-8 White
    }

    private void OnPlayerSpawn(On.Celeste.Pico8.Classic.player_spawn.orig_ctor orig, Classic.player_spawn self)
    {
        orig(self);
        _inGame = true;
        _alive = true;
        Context.Main.SendState();
    }

    private void OnEmulatorUpdate(On.Celeste.Pico8.Emulator.orig_Update orig, Emulator self)
    {
        if (!Settings.Connected) {
            Ghosts.Clear();
        }

        if (!(Context?.Chat?.Active ?? false)) {
            orig(self);
        }
    }

    private void OnPlayerDraw(On.Celeste.Pico8.Classic.player.orig_draw orig, Classic.player self)
    {
        lock (Ghosts) {
            foreach (PicoGhost ghost in Ghosts.Values) {
                ghost.draw();
            }
        }

        orig(self);
        if (!Settings.InGameHUD.ShowOwnName) return;
        
        var name = Client?.PlayerInfo?.DisplayName ?? "You";
        var colonIndex = name.LastIndexOf(':');
        if (colonIndex > -1)
            name = name[(colonIndex + 2)..];
        Pico8FontHelper.PrintOutlinedCenter(
            name, (int) self.x + 4, (int) self.y - 8
        );
    }

    private void OnEmulatorReset(On.Celeste.Pico8.Emulator.orig_ResetScreen orig, Emulator self)
    {
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"EMULATOR RESET");
        _alive = false;
        _inGame = false;
        Client?.Send(new DataPicoFrame() {Player = Client?.PlayerInfo, Level = -1});
        orig(self);
        _classic = null;
        _classicData = null;
        _emulatorData = null;
        Context.Main.SendState();
    }

    private void OnPlayerKill(On.Celeste.Pico8.Classic.orig_kill_player orig, Classic self, Classic.player obj)
    {
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"PLAYER DEAD");
        _alive = false;
        Client?.Send(new DataPicoFrame() {Player = Client?.PlayerInfo, Dead = true});
        orig(self, obj);
    }

    private void OnDrawHair(On.Celeste.Pico8.Classic.player_hair.orig_draw_hair orig, Classic.player_hair self, Classic.ClassicObject obj, int facing, int djump)
    {
        orig(self, obj, facing, djump);

        // FIXME: This kinda sucks. Making a new DynamicData 7 times an update is bad for performance.
        #nullable disable
        DynamicData data = new(self);
        // This type is private :/
        object[] hairNodes = (object[]) data.Get("hair");
        if (hairNodes == null) return;

        _queuedGhostFrame.Hair.Clear();
        foreach (object node in hairNodes) {
            DynamicData nodeData = new(node);

            var x = (float?) nodeData.Get("x") ?? float.NaN;
            var y = (float?) nodeData.Get("y") ?? float.NaN;
            var size = (float?) nodeData.Get("size") ?? float.NaN;

            _queuedGhostFrame.Hair.Add(new DataPicoFrame.HairNode {
                X = x,
                Y = y,
                Size = size
            });
        }
        #nullable enable
    }

    private void OnPlayerCreate(On.Celeste.Pico8.Classic.player.orig_ctor orig, Classic.player self)
    {
        uint id = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"SELF ID: {id}");
        _alive = true;
        Context.Main.SendState();
        orig(self);
    }

    private int LevelIndex {get {
        if (!InitData()) return -1;
        return (int?) _classicData?.Invoke("level_index") ?? -1;
    }}

    private void OnPlayerSpawnUpdate(On.Celeste.Pico8.Classic.player_spawn.orig_update orig, Classic.player_spawn self)
    {
        _inGame = true;
        _alive = true;

        _queuedGhostFrame.Spr = self.spr;
        _queuedGhostFrame.FlipX = self.flipX;
        _queuedGhostFrame.FlipY = self.flipY;
        _queuedGhostFrame.X = self.x;
        _queuedGhostFrame.Y = self.y;
        _queuedGhostFrame.Type = self.type;

        orig(self);
    }

    private void OnPlayerUpdate(On.Celeste.Pico8.Classic.player.orig_update orig, Classic.player self)
    {
        _inGame = true;
        _alive = true;

        _queuedGhostFrame.Spr = self.spr;
        _queuedGhostFrame.DJump = self.djump;
        _queuedGhostFrame.FlipX = self.flipX;
        _queuedGhostFrame.FlipY = self.flipY;
        _queuedGhostFrame.X = self.x;
        _queuedGhostFrame.Y = self.y;
        _queuedGhostFrame.Type = self.type;

        orig(self);
    }

    private void OnEmulatorClose(On.Celeste.Pico8.Emulator.orig_End orig, Emulator self)
    {
        _alive = false;
        _inGame = false;
        _classicData = null;
        _emulatorData = null;
        _classic = null;
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"CLOSING EMULATOR");
        Context.Main.SendState();
        orig(self);
    }

    #endregion

    private DynamicData? _classicData;
    private DynamicData? _emulatorData;
    private Classic? _classic;

    // This initializes the top three if it returns true.
    private bool InitData(bool reinit = false) {
        if (Engine.Scene is not Emulator emu) return false;
        if (reinit) {
            _emulatorData = null;
            _classicData = null;
            _classic = null;
        }
        
        _emulatorData ??= DynamicData.For(emu);
        _classic ??= (Classic?) _emulatorData.Get("game");
        if (_classic != null) {
            _classicData ??= DynamicData.For(_classic);
        }

        return !(_classic == null || _emulatorData == null || _classicData == null);
    }

    private bool _inGame;

    public override void Update(GameTime gameTime) {
        base.Update(gameTime);

        if (Engine.Scene is not Emulator) {
            Ghosts.Clear();
            return;
        }

        if (!Settings.Connected) return;
        if (!_alive) {
            Ghosts.Clear();
            return;
        }

        lock (Ghosts) {
            foreach (KeyValuePair<uint, PicoGhost> kvp in Ghosts) {
                PruneIfInactive(kvp.Key, kvp.Value);
            }
        }

        if (!InitData()) return;

        var objs = (List<Classic.ClassicObject>?) _classicData?.Get("objects");
        if (objs == null) return;

        lock (objs) {
            if (!objs.Any(o => o is Classic.player || o is Classic.player_spawn)) {
                Logger.Log(LogLevel.DBG, "PICO8-CNET", $"NO PLAYER FOUND");
                _alive = false;
                return;
            }
        }

        var state = new DataPicoFrame {
            Player = Client?.PlayerInfo,
            Spr = _queuedGhostFrame.Spr,
            X = _queuedGhostFrame.X,
            Y = _queuedGhostFrame.Y,
            FlipX = _queuedGhostFrame.FlipX,
            FlipY = _queuedGhostFrame.FlipY,
            Type = _queuedGhostFrame.Type,
            DJump = _queuedGhostFrame.DJump,
            Hair = _queuedGhostFrame.Hair,
            Level = LevelIndex,
        };

        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"SEND {state}");

        Client?.Send(state);
    }

    private void PruneIfInactive(uint id, PicoGhost ghost)
    {
        bool active = 
            Settings.Connected &&
            _inGame &&
            ghost.Player.ID == id &&
            (Client?.Data?.TryGetBoundRef(ghost.Player, out DataPlayerState? state) ?? false) &&
            state is { SID: "PICO-8" } &&
            LevelIndex != -1 &&
            state.Level == $"{(LevelIndex + 1) * 100}M";
            // TODO: need more checks!
        
        if (!active) {
            Ghosts.TryRemove(id, out _);
        }
    }

    private PicoGhost? InitGhost(DataPlayerInfo player) {
        if (Engine.Scene is not Emulator emu) {
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "Tried to initialize pico ghost in non-Emulator");
            return null;
        }
        
        if (!InitData()) {
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "Failed to InitData");
            return null;
        }

        if (_classic == null) {
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "Failed to retrieve Classic");
            return null;
        }

        var ghost = new PicoGhost(player, this);
        ghost.init(_classic, emu);
        Ghosts[player.ID] = ghost;
        return ghost;
    
    }

    #region Handlers

    public void Handle (CelesteNetConnection con, DataPicoFrame frame) {

        uint ownId = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        if (frame.Player?.ID == ownId) return;
        if (Engine.Scene is not Emulator) return;
        if (LevelIndex == -1) return;
        if (frame.Player == null) return;
        if (!_alive) return;

        if (!Ghosts.TryGetValue(frame.Player.ID, out var ghost)) {
            Logger.Log(LogLevel.DBG, "PICO8-CNET", $"CREATE {frame.Player.ID}");
            ghost = InitGhost(frame.Player);
            lock (Ghosts) {
                Logger.Log(LogLevel.DBG, "PICO8-CNET", $"GHOSTS: {string.Join(", ", Ghosts.Values.Select(i => i.ToString()))}");
            }
        }

        if (ghost == null) {
            Logger.Log(LogLevel.ERR, "PICO8-CNET", "Ghost is null after InitGhost was called. This should never happen!");
            lock (Ghosts) {
                Logger.Log(LogLevel.ERR, "PICO8-CNET", $"Ghosts: {string.Join(", ", Ghosts.Values.Select(i => i.ToString()))}");
            }
            return;
        }
        
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"RECV {frame}");
        
        if (frame.Dead) {
            if (Ghosts.TryRemove(ghost.Player.ID, out _)) {
                PlayGhostKill(ghost);
            }
            return;
        }

        if (frame.Level != LevelIndex) {
            Ghosts.TryRemove(ghost.Player.ID, out _);
            return;
        }
        
        ghost.x = frame.X;
        ghost.y = frame.Y;
        ghost.flipX = frame.FlipX;
        ghost.flipY = frame.FlipY;
        ghost.DJump = frame.DJump;
        ghost.spr = frame.Spr;
        ghost.type = frame.Type;
        ghost.Hair = frame.Hair;
    }

    // Unfortunately, there's no util in DynamicData for private classes. To reflection, we go!
    private static readonly Type? DeadParticle = typeof(Classic)
        .GetNestedType("DeadParticle", BindingFlags.NonPublic);

    private void PlayGhostKill (PicoGhost ghost) {
        if (DeadParticle == null) return;

        object? deadParticles = _classicData?.Get("dead_particles");
        if (deadParticles == null) return;
        // 7 DynamicData's isn't great, but this is called once every few seconds at most. It's probably fine?
        DynamicData deadParticlesData = new(deadParticles);

        deadParticlesData.Invoke("Clear");
        for (int i = 0; i <= 7; i++) {
            float num = i / 8f;
            object? particle = Activator.CreateInstance(DeadParticle);
            if (particle == null) { continue; }
            DynamicData particleData = new(particle);
            particleData.Set("x", ghost.x + 4f);
            particleData.Set("y", ghost.y + 4f);
            particleData.Set("t", 10);
            particleData.Set("spd", new Vector2(
                (float) Math.Cos((double) num * -2f * (float) Math.PI) * 3f,
                (float) Math.Sin(((double) num + 0.5f) * -2f * (float) Math.PI) * 3f) 
            );

            deadParticlesData.Invoke("Add", particle);
        }
    }

    #endregion
}