using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Pico8;
using FMOD;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.CelesteNet.Client.Components;

internal class GhostState
{
    internal float spr;
    internal float x;
    internal float y;
    internal bool flipX;
    internal bool flipY;
    internal int type;
    internal int djump;
    internal DataPicoState.HairNode[] hair = new DataPicoState.HairNode[5] {
        new(), new(), new(), new(), new()
    };
}

public class CelesteNetPico8Component : CelesteNetGameComponent {
    
    private readonly Dictionary<uint, PicoGhost> ghosts = new();

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
                On.Celeste.Pico8.Classic.player.update += OnPlayerUpdate;
                On.Celeste.Pico8.Classic.kill_player += OnPlayerKill;
                On.Celeste.Pico8.Classic.player_hair.draw_hair += OnDrawHair;
                On.Celeste.Pico8.Emulator.End += OnEmulatorClose;

            }
        });
        #pragma warning restore CA2012
    }

    #region Hooks

    private void OnPlayerKill(On.Celeste.Pico8.Classic.orig_kill_player orig, Classic self, Classic.player obj)
    {
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"PLAYER DEAD");
        alive = false;
        Client?.Send(new DataPicoEnd() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
        orig(self, obj);
    }

    private void OnDrawHair(On.Celeste.Pico8.Classic.player_hair.orig_draw_hair orig, Classic.player_hair self, Classic.ClassicObject obj, int facing, int djump)
    {
        orig(self, obj, facing, djump);

        // TODO: This kinda sucks. Making a new dynamicdata 7 times an update is bad for performance.

        DynamicData data = new(self);
        // This type is private :/
        object[] hairNodes = (object[]) data.Get("hair");

        for (int i = 0; i < 5; i++) {
            object node = hairNodes[i];
            DynamicData nodeData = new(node);

            var x = (float) nodeData.Get("x");
            var y = (float) nodeData.Get("y");
            var size = (float) nodeData.Get("size");

            queuedGhostState.hair[i].X = x;
            queuedGhostState.hair[i].Y = y;
            queuedGhostState.hair[i].Size = size;
        }
    }

    private void OnPlayerCreate(On.Celeste.Pico8.Classic.player.orig_ctor orig, Classic.player self)
    {
        uint id = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"SELF ID: {id}");
        DeleteGhosts();
        alive = true;
        orig(self);
    }

    private void OnPlayerUpdate(On.Celeste.Pico8.Classic.player.orig_update orig, Classic.player self)
    {

        queuedGhostState.spr = self.spr;
        queuedGhostState.djump = self.djump;
        queuedGhostState.flipX = self.flipX;
        queuedGhostState.flipY = self.flipY;
        queuedGhostState.x = self.x;
        queuedGhostState.y = self.y;
        queuedGhostState.type = self.type;

        orig(self);
    }

    private void OnEmulatorClose(On.Celeste.Pico8.Emulator.orig_End orig, Emulator self)
    {
        alive = false;
        classicData = null;
        emulatorData = null;
        classic = null;
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"CLOSING EMULATOR");
        Client?.Send(new DataPicoEnd() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
        DeleteGhosts();
        orig(self);
    }

    #endregion

    #nullable enable

    private void DeleteGhosts() {
        if (!InitData()) { return; }
        
        var keys = ghosts.Keys.ToArray();
        foreach (uint ID in keys) {
            ghosts.Remove(ID, out PicoGhost? ghost);
            if (ghost == null) { continue; }

            classicData?.Invoke("destroy_object", new object[] { ghost });
        }
    }

    DynamicData? classicData;
    DynamicData? emulatorData;
    Classic? classic;

    // This initializes the top three if it returns true.
    private bool InitData() {
        if (Engine.Scene is not Emulator emu) {
            return false;
        }

        if (classicData == null) {
            emulatorData ??= DynamicData.For(emu);
            classic = (Classic?) emulatorData.Get("game");
            if (classic == null) { return false; }
            classicData = DynamicData.For(classic);
        }

        return true;
    }

    // This is kinda heavy, should only be run every so often
    private void CollectDeadGhosts() {
        if (!InitData()) { return; };



        var objs = (List<Classic.ClassicObject>?) classicData?.Get("objects");
        if (objs == null) { return; }

        var queuedForDeletion = new List<uint>();

        // O(m*n), where m is # of objects and n is # of ghosts, shouldn't get larger than 5000 worst case?
        lock (ghosts) {
            foreach (KeyValuePair<uint, PicoGhost> kvp in ghosts) {
                if (!objs.Contains(kvp.Value)) {
                    queuedForDeletion.Add(kvp.Key);
                }
            }
        }

        Logger.Log(LogLevel.INF, "PICO8-CNET", $"COLLECTED {queuedForDeletion.Count} DEAD GHOSTS");

        foreach (uint ID in queuedForDeletion) {
            ghosts.Remove(ID);
        }
    }

    public override void Update(GameTime gameTime) {
        base.Update(gameTime);

        if (Engine.Scene is not Emulator emu) {
            DeleteGhosts();
            return;
        }

        if (!InitData()) { return; };

        var objs = (List<Classic.ClassicObject>?) classicData?.Get("objects");
        if (objs == null) { return; }
        if (!alive) { return; }

        lock (objs) {
            if (!objs.Any(o => o is Classic.player)) {
                Logger.Log(LogLevel.INF, "PICO8-CNET", $"NO PLAYER FOUND");
                alive = false;
                DeleteGhosts();
                Client?.Send(new DataPicoEnd() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
                return;
            }
        }

        var state = new DataPicoState {
            ID = Client?.PlayerInfo?.ID ?? uint.MaxValue,
            Spr = queuedGhostState.spr,
            X = queuedGhostState.x,
            Y = queuedGhostState.y,
            FlipX = queuedGhostState.flipX,
            FlipY = queuedGhostState.flipY,
            Type = queuedGhostState.type,
            Djump = queuedGhostState.djump,
            Hair = queuedGhostState.hair
        };

        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"SEND {state}");

        Client?.Send(state);
    }


    public PicoGhost? InitGhost(uint ID) {
        if (!InitData()) { return null; };

        CollectDeadGhosts();

        if (Engine.Scene is Emulator emu) {
            if (classic == null) { return null; }
            var objs = (List<Classic.ClassicObject>?) classicData?.Get("objects");
            if (objs == null) {
                Logger.Log(LogLevel.WRN, "PICO8-CNET", "Failed to retrieve Classic.objects");
                return null;
            }
            var ghost = new PicoGhost();
            ghost.init(classic, emu);
            objs.Add(ghost);
            ghosts[ID] = ghost;
            return ghost;
        }
        return null;
    }

    public DateTime lastLog = DateTime.UnixEpoch;

    public void Handle (CelesteNetConnection con, DataPicoState state) {

        uint ownID = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        if (state.ID == ownID) { return; }
        if (Engine.Scene is not Emulator) { return; }

        if (!ghosts.TryGetValue(state.ID, out PicoGhost? ghost)) {
            Logger.Log(LogLevel.INF, "PICO8-CNET", $"CREATE {state.ID}");
            ghost = InitGhost(state.ID);
            Logger.Log(LogLevel.INF, "PICO8-CNET", $"GHOSTS: {string.Join(", ", ghosts.Keys.Select(i => i.ToString()))}");
        };

        if (ghost == null) {
            throw new Exception("Ghost is null after InitGhost was called. This should never happen!");
        }
        
        Logger.Log(LogLevel.DBG, "PICO8-CNET", $"RECV {state}");
        
        ghost.id = state.ID;
        ghost.x = state.X;
        ghost.y = state.Y;
        ghost.flipX = state.FlipX;
        ghost.flipY = state.FlipY;
        ghost.djump = state.Djump;
        ghost.spr = state.Spr;
        ghost.type = state.Type;
        ghost.hair = state.Hair;
    }

    public void Handle (CelesteNetConnection con, DataPicoEnd state) {
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"END {state.ID}");
        if (!InitData()) { return; };

        uint ownID = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        if (state.ID == ownID) { return; }
        ghosts.Remove(state.ID, out PicoGhost? ghost);
        if (ghost == null) {
            Logger.Log(LogLevel.WRN, "PICO8-CNET", $"GHOST {state.ID} ENDED WITHOUT EXISTING");
            Logger.Log(LogLevel.WRN, "PICO8-CNET", $"GHOSTS: {string.Join(", ", ghosts.Keys.Select(i => i.ToString()))}");
            return;
        }

        classicData?.Invoke("destroy_object", new object[] { ghost });

        CollectDeadGhosts();
    }
}