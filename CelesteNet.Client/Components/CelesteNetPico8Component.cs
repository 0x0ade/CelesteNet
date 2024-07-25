using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Pico8;
using FMOD;
using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
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
}

public class CelesteNetPico8Component : CelesteNetGameComponent {
    
    static readonly FieldInfo game = 
        typeof(Emulator)
        .GetField("game", BindingFlags.Instance | BindingFlags.NonPublic);

    static readonly FieldInfo objects = 
        typeof(Classic)
        .GetField("objects", BindingFlags.Instance | BindingFlags.NonPublic);

    static readonly MethodInfo destroy_object = 
        typeof(Classic)
        .GetMethod("destroy_object", BindingFlags.Instance | BindingFlags.NonPublic);
    
    private readonly Dictionary<uint, PicoGhost> ghosts = new();

    static readonly GhostState queuedGhostState = new();

    public CelesteNetPico8Component(CelesteNetClientContext context, Game game) : base(context, game) {}

    public override void Initialize() {
        base.Initialize();

        #pragma warning disable CA2012
        MainThreadHelper.Schedule(() =>
        {
            // modern monomod does detourcontexts differently
            using (new DetourConfigContext(new DetourConfig(
                "CelesteNetPico8",
                int.MinValue  // this simulates before: "*"
            )).Use())
            {
                On.Celeste.Pico8.Classic.player.ctor += OnPlayerConstruct;
                On.Celeste.Pico8.Classic.kill_player += OnPlayerKill;
                On.Celeste.Pico8.Classic.player.update += OnPlayerUpdate;
                On.Celeste.Pico8.Emulator.End += OnEmulatorClose;

            }
        });
        #pragma warning restore CA2012
    }

    private void OnPlayerUpdate(On.Celeste.Pico8.Classic.player.orig_update orig, Classic.player self)
    {
        orig(self);
        queuedGhostState.spr = self.spr;
        queuedGhostState.djump = self.djump;
        queuedGhostState.flipX = self.flipX;
        queuedGhostState.flipY = self.flipY;
        queuedGhostState.x = self.x;
        queuedGhostState.y = self.y;
        queuedGhostState.type = self.type;
    }

    private void OnEmulatorClose(On.Celeste.Pico8.Emulator.orig_End orig, Emulator self)
    {
        Client?.Send(new DataPicoEnd() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
        ghosts.Clear();
        orig(self);
    }

    private void OnPlayerConstruct(On.Celeste.Pico8.Classic.player.orig_ctor orig, Classic.player self)
    {
        orig(self);
        Client?.Send(new DataPicoCreate() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
    }


    private void OnPlayerKill(On.Celeste.Pico8.Classic.orig_kill_player orig, Classic self, Classic.player obj)
    {
        Client?.Send(new DataPicoEnd() {ID = Client?.PlayerInfo?.ID ?? uint.MaxValue});
        orig(self, obj);
    }

    public void SendPicoState() {
        Client?.SendAndHandle(new DataPicoState {
            ID = Client?.PlayerInfo?.ID ?? uint.MaxValue,
            Spr = queuedGhostState.spr,
            X = queuedGhostState.x,
            Y = queuedGhostState.y,
            FlipX = queuedGhostState.flipX,
            FlipY = queuedGhostState.flipY,
            Type = queuedGhostState.type,
            Djump = queuedGhostState.djump,
        });
    }

    public override void Update(GameTime gameTime) {
        base.Update(gameTime);

        if (Engine.Scene is not Emulator) {
            ghosts.Clear();
            return;
        }

        SendPicoState();
    }

    #nullable enable

    public void Handle (CelesteNetConnection con, DataPicoCreate state) {
        if (state.ID == Client?.PlayerInfo?.ID) { return; }
        if (Engine.Scene is Emulator emu) {
            Classic? classic = (Classic?) game.GetValue(emu);
            if (classic == null) { return; }
            var objs = (List<Classic.ClassicObject>?) objects.GetValue(classic);
            if (objs == null) {
                Logger.Log(LogLevel.WRN, "PICO8-CNET", "Failed to retrieve Classic.objects");
                return;
            }
            var ghost = new PicoGhost();
            ghost.init(classic, emu);
            objs.Add(ghost);
            ghosts.Add(state.ID, ghost);
        }
    }

    public void Handle (CelesteNetConnection con, DataPicoState state) {
        if (state.ID == Client?.PlayerInfo?.ID) { return; }
        if (Engine.Scene is not Emulator emu) { return; }

        if (!ghosts.TryGetValue(state.ID, out PicoGhost? ghost)) {
            Classic? classic = (Classic?)game.GetValue(emu);
            if (classic == null) { return; }
            var objs = (List<Classic.ClassicObject>?)objects.GetValue(classic);
            if (objs == null)
            {
                Logger.Log(LogLevel.WRN, "PICO8-CNET", "Failed to retrieve Classic.objects");
                return;
            }

            ghost = new PicoGhost();
            ghost.init(classic, emu);
            objs.Add(ghost);
            ghosts.Add(state.ID, ghost);
        };

        if (ghost == null) { return; }
        
        ghost.x = state.X;
        ghost.y = state.Y;
        ghost.flipX = state.FlipX;
        ghost.flipY = state.FlipY;
        ghost.djump = state.Djump;
        ghost.spr = state.Spr;
        ghost.type = state.Type; 
    }

    public void Handle (CelesteNetConnection con, DataPicoEnd state) {
        if (state.ID == Client?.PlayerInfo?.ID) { return; }
        ghosts.Remove(state.ID, out PicoGhost? ghost);
        if (ghost == null) { return; }
        destroy_object.Invoke(ghost.G, new object[] { ghost });
    }

}