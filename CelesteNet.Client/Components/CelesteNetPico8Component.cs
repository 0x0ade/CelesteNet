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
using System.Reflection;

namespace Celeste.Mod.CelesteNet.Client.Components;

internal record GhostState
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
                On.Celeste.Pico8.Classic.player.ctor += OnPlayerConstruct;
                On.Celeste.Pico8.Classic.player.update += OnPlayerUpdate;
                On.Celeste.Pico8.Emulator.End += OnEmulatorClose;

            }
        });
        #pragma warning restore CA2012
    }


    private void OnPlayerUpdate(On.Celeste.Pico8.Classic.player.orig_update orig, Classic.player self)
    {
        // TODO: This kinda sucks. Making a new dynamicdata 7 times an update is bad for performance.

        orig(self);
        queuedGhostState.spr = self.spr;
        queuedGhostState.djump = self.djump;
        queuedGhostState.flipX = self.flipX;
        queuedGhostState.flipY = self.flipY;
        queuedGhostState.x = self.x;
        queuedGhostState.y = self.y;
        queuedGhostState.type = self.type;

        DynamicData data = new(self.hair);
        // This type is private :/
        object[] hairNodes = (object[]) data.Get("hair");

        for (int i = 0; i < 5; i++) {
            object node = hairNodes[i];
            DynamicData nodeData = new(node);

            queuedGhostState.hair[i].X = (float) nodeData.Get("X");
            queuedGhostState.hair[i].Y = (float) nodeData.Get("Y");
            queuedGhostState.hair[i].Size = (float) nodeData.Get("Size");
        }
    }

    private void OnEmulatorClose(On.Celeste.Pico8.Emulator.orig_End orig, Emulator self)
    {
        alive = false;
        classicData = null;
        emulatorData = null;
        classic = null;
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"CLOSING EMULATOR");
        Client?.Send(new DataPicoEnd() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
        ghosts.Clear();
        orig(self);
    }

    private void OnPlayerConstruct(On.Celeste.Pico8.Classic.player.orig_ctor orig, Classic.player self)
    {
        orig(self);
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"CREATING PLAYER");
        alive = true;
        Client?.Send(new DataPicoCreate() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
    }

    #nullable enable

    DynamicData? classicData;
    DynamicData? emulatorData;
    Classic? classic;

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

    public override void Update(GameTime gameTime) {
        base.Update(gameTime);

        if (Engine.Scene is not Emulator emu) {
            ghosts.Clear();
            return;
        }

        if (!InitData()) { return; };

        var objs = (List<Classic.ClassicObject>?) classicData.Get("objects");
        if (objs == null) { return; }
        if (!alive) { return; }

        if (!objs.Any(o => o is Classic.player)) {
            alive = false;
            Logger.Log(LogLevel.INF, "PICO8-CNET", $"NO PLAYER FOUND");
            Client?.Send(new DataPicoEnd() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
            return;
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

        Logger.Log(LogLevel.INF, "PICO8-CNET", $"SEND {state}");

        Client?.Send(state);
    }


    public PicoGhost? InitGhost(uint ID) {
        if (!InitData()) { return null; };

        if (Engine.Scene is Emulator emu) {
            if (classic == null) { return null; }
            var objs = (List<Classic.ClassicObject>?) classicData.Get("objects");
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


    public void Handle (CelesteNetConnection con, DataPicoCreate state) {
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"CREATE {state.ID}");

        uint id = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        if (state.ID == id) { return; }
        InitGhost(id);
    }

    public DateTime lastLog = DateTime.UnixEpoch;

    public void Handle (CelesteNetConnection con, DataPicoState state) {

        uint id = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        if (state.ID == id) { return; }
        if (Engine.Scene is not Emulator) { return; }

        if (!ghosts.TryGetValue(id, out PicoGhost? ghost)) {
            Logger.Log(LogLevel.INF, "PICO8-CNET", $"INIT {state.ID}");
            ghost = InitGhost(id);
        };

        if (ghost == null) { return; }
        
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"RECV {state}");
        
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

        uint id = Client?.PlayerInfo?.ID ?? uint.MaxValue;
        if (state.ID == id) { return; }
        ghosts.Remove(state.ID, out PicoGhost? ghost);
        if (ghost == null) { return; }

        classicData.Invoke("destroy_object", new object[] { ghost });
    }
}