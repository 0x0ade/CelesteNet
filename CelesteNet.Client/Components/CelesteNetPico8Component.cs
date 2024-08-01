using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Pico8;
using FMOD;
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


internal class GhostState
{
    internal float spr;
    internal float x;
    internal float y;
    internal bool flipX;
    internal bool flipY;
    internal int type;
    internal int djump;
    internal List<DataPicoState.HairNode> hair = new();
    internal int level;
}

public class CelesteNetPico8Component : CelesteNetGameComponent {
    
    public readonly ConcurrentDictionary<uint, PicoGhost> ghosts = new();

    readonly GhostState queuedGhostState = new();
    public bool alive = false;

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

    private static void SendState() {
        var main =
            CelesteNetClientModule.Instance
                .Context
                .Get<CelesteNetMainComponent>();
        
        main.SendState();
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
        ghosts.Clear();
    }

    private void ModifyStateInPico(DataPlayerState state)
    {
        uint? id = state.Player?.ID;
        if (id == null) { return; }
        if (!inGame) { return; }

        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"Modifying state!");

        state.SID = $"PICO-8";
        state.Mode = AreaMode.Normal;
        state.Interactive = false;
        state.Idle = false;
        
        int index = LevelIndex;
        if (index == -1) { return; }

        state.Level = $"{(index + 1) * 100}M";
    }

    private void ModifyStateOfPicoPlayers(CelesteNetPlayerListComponent.BlobPlayer player, DataPlayerState state)
    {
        if (state.SID != "PICO-8") {return;}
        
        player.Location.Icon = "menu/pico8";
        player.Location.Side = "";
        player.Location.TitleColor = FontHelper.PicoWhite;
    }

    private void OnPlayerSpawn(On.Celeste.Pico8.Classic.player_spawn.orig_ctor orig, Classic.player_spawn self)
    {
        orig(self);
        inGame = true;
        alive = true;
        SendState();
    }

    private void OnEmulatorUpdate(On.Celeste.Pico8.Emulator.orig_Update orig, Emulator self)
    {
        if (!Settings.Connected) {
            ghosts.Clear();
        }

        if (!(Context?.Chat?.Active ?? false)) {
            orig(self);
        }
    }

    private void OnPlayerDraw(On.Celeste.Pico8.Classic.player.orig_draw orig, Classic.player self)
    {
        lock (ghosts) {
            foreach (PicoGhost ghost in ghosts.Values) {
                ghost.draw();
            }
        }

        orig(self);
        if (Settings.InGameHUD.ShowOwnName) {
            var name = Client?.PlayerInfo?.DisplayName ?? "You";
            var colonIndex = name.LastIndexOf(':');
            if (colonIndex > -1) {
                name = name[(colonIndex + 2)..];
            }
            FontHelper.PrintOutlinedCenter(
                name, (int) self.x + 4, (int) self.y - 8
            );
        }
    }

    private void OnEmulatorReset(On.Celeste.Pico8.Emulator.orig_ResetScreen orig, Emulator self)
    {
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"EMULATOR RESET");
        alive = false;
        inGame = false;
        Client?.Send(new DataPicoState() {Player = Client?.PlayerInfo, Level = -1});
        orig(self);
        classic = null;
        classicData = null;
        emulatorData = null;
        SendState();
    }

    private void OnPlayerKill(On.Celeste.Pico8.Classic.orig_kill_player orig, Classic self, Classic.player obj)
    {
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"PLAYER DEAD");
        alive = false;
        Client?.Send(new DataPicoState() {Player = Client?.PlayerInfo, Dead = true});
        orig(self, obj);
    }

    private void OnDrawHair(On.Celeste.Pico8.Classic.player_hair.orig_draw_hair orig, Classic.player_hair self, Classic.ClassicObject obj, int facing, int djump)
    {
        orig(self, obj, facing, djump);

        // TODO: This kinda sucks. Making a new dynamicdata 7 times an update is bad for performance.
        #nullable disable
        DynamicData data = new(self);
        // This type is private :/
        object[] hairNodes = (object[]) data.Get("hair");

        queuedGhostState.hair.Clear();
        foreach (object node in hairNodes) {
            DynamicData nodeData = new(node);

            var x = (float) nodeData.Get("x");
            var y = (float) nodeData.Get("y");
            var size = (float) nodeData.Get("size");

            queuedGhostState.hair.Add(new() {
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
        alive = true;
        SendState();
        orig(self);
    }

    private int LevelIndex {get {
        if (!InitData()) {return -1;}
        return (int?) classicData?.Invoke("level_index") ?? -1;
    }}

    private void OnPlayerSpawnUpdate(On.Celeste.Pico8.Classic.player_spawn.orig_update orig, Classic.player_spawn self)
    {
        inGame = true;
        alive = true;

        queuedGhostState.spr = self.spr;
        queuedGhostState.flipX = self.flipX;
        queuedGhostState.flipY = self.flipY;
        queuedGhostState.x = self.x;
        queuedGhostState.y = self.y;
        queuedGhostState.type = self.type;
        queuedGhostState.level = LevelIndex;

        orig(self);
    }

    private void OnPlayerUpdate(On.Celeste.Pico8.Classic.player.orig_update orig, Classic.player self)
    {
        inGame = true;
        alive = true;

        queuedGhostState.spr = self.spr;
        queuedGhostState.djump = self.djump;
        queuedGhostState.flipX = self.flipX;
        queuedGhostState.flipY = self.flipY;
        queuedGhostState.x = self.x;
        queuedGhostState.y = self.y;
        queuedGhostState.type = self.type;
        queuedGhostState.level = LevelIndex;

        orig(self);
    }

    private void OnEmulatorClose(On.Celeste.Pico8.Emulator.orig_End orig, Emulator self)
    {
        alive = false;
        inGame = false;
        classicData = null;
        emulatorData = null;
        classic = null;
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"CLOSING EMULATOR");
        SendState();
        orig(self);
    }

    #endregion

    DynamicData? classicData;
    DynamicData? emulatorData;
    Classic? classic;

    // This initializes the top three if it returns true.
    private bool InitData(bool reinit = false) {
        if (Engine.Scene is not Emulator emu) {
            return false;
        }
        if (reinit) {
            emulatorData = null;
            classicData = null;
            classic = null;
        }
        
        emulatorData ??= DynamicData.For(emu);
        classic ??= (Classic?) emulatorData.Get("game");
        if (classic != null) {
            classicData ??= DynamicData.For(classic);
        }

        return !(classic == null || emulatorData == null || classicData == null);
    }

    private bool inGame;

    public override void Update(GameTime gameTime) {
        base.Update(gameTime);

        if (Engine.Scene is not Emulator emu) {
            ghosts.Clear();
            return;
        }

        if (!Settings.Connected) { return; }
        if (!alive) {
            ghosts.Clear();
            return;
        }

        lock (ghosts) {
            foreach (KeyValuePair<uint, PicoGhost> kvp in ghosts) {
                PruneIfInactive(kvp.Key, kvp.Value);
            }
        }

        if (!InitData()) { return; };

        var objs = (List<Classic.ClassicObject>?) classicData?.Get("objects");
        if (objs == null) { return; }

        lock (objs) {
            if (!objs.Any(o => o is Classic.player || o is Classic.player_spawn)) {
                Logger.Log(LogLevel.DBG, "PICO8-CNET", $"NO PLAYER FOUND");
                alive = false;
                return;
            }
        }

        var state = new DataPicoState {
            Player = Client?.PlayerInfo,
            Spr = queuedGhostState.spr,
            X = queuedGhostState.x,
            Y = queuedGhostState.y,
            FlipX = queuedGhostState.flipX,
            FlipY = queuedGhostState.flipY,
            Type = queuedGhostState.type,
            Djump = queuedGhostState.djump,
            Hair = queuedGhostState.hair,
            Level = LevelIndex,
        };

        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"SEND {state}");

        Client?.Send(state);
    }

    private void PruneIfInactive(uint id, PicoGhost ghost)
    {
        bool active = 
            Settings.Connected &&
            inGame &&
            ghost.Player.ID == id &&
            (Client?.Data?.TryGetBoundRef(ghost.Player, out DataPlayerState? state) ?? false) &&
            state != null &&
            state.SID == "PICO-8" &&
            LevelIndex != -1 &&
            state.Level == $"{(LevelIndex + 1) * 100}M";
            // TODO: need more checks!
        
        if (!active) {
            ghosts.TryRemove(id, out _);
        }
    }

    public PicoGhost? InitGhost(DataPlayerInfo player) {
        if (Engine.Scene is not Emulator emu) {
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "Tried to initialize pico ghost in non-Emulator");
            return null;
        }
        
        if (!InitData()) {
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "Failed to InitData");
            return null;
        };

        if (classic == null) {
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "Failed to retrieve Classic");
            return null;
        }

        var ghost = new PicoGhost(player, this);
        ghost.init(classic, emu);
        ghosts[player.ID] = ghost;
        return ghost;
    
    }

    #region Handlers

    public void Handle (CelesteNetConnection con, DataPicoState state) {

        uint ownID = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        if (state.Player?.ID == ownID) { return; }
        if (Engine.Scene is not Emulator) { return; }
        if (LevelIndex == -1) { return; }
        if (state.Player == null) { return; }
        if (!alive) { return; }

        if (!ghosts.TryGetValue(state.Player.ID, out PicoGhost? ghost) || ghost == null) {
            Logger.Log(LogLevel.DBG, "PICO8-CNET", $"CREATE {state.Player.ID}");
            ghost = InitGhost(state.Player);
            lock (ghosts) {
                Logger.Log(LogLevel.DBG, "PICO8-CNET", $"GHOSTS: {string.Join(", ", ghosts.Values.Select(i => i.ToString()))}");
            }
        };

        if (ghost == null) {
            Logger.Log(LogLevel.ERR, "PICO8-CNET", "Ghost is null after InitGhost was called. This should never happen!");
            lock (ghosts) {
                Logger.Log(LogLevel.ERR, "PICO8-CNET", $"Ghosts: {string.Join(", ", ghosts.Values.Select(i => i.ToString()))}");
            }
            return;
        }
        
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"RECV {state}");
        
        if (state.Dead) {
            if (ghosts.TryRemove(ghost.Player.ID, out _)) {
                PlayGhostKill(ghost);
            }
            return;
        }

        if (state.Level != LevelIndex) {
            ghosts.TryRemove(ghost.Player.ID, out _);
            return;
        }
        
        ghost.x = state.X;
        ghost.y = state.Y;
        ghost.flipX = state.FlipX;
        ghost.flipY = state.FlipY;
        ghost.djump = state.Djump;
        ghost.spr = state.Spr;
        ghost.type = state.Type;
        ghost.hair = state.Hair;
    }

    // Unfortunately, there's no util in DynamicData for private classes. To reflection we go!
    static readonly Type? DeadParticle = typeof(Classic)
        .GetNestedType("DeadParticle", BindingFlags.NonPublic);

    public void PlayGhostKill (PicoGhost ghost) {
        if (DeadParticle == null) { return; }

        object? deadParticles = classicData?.Get("dead_particles");
        if (deadParticles == null) { return; }
        // 7 DynamicDatas isn't great, but this is called once every few seconds at most. It's probably fine?
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