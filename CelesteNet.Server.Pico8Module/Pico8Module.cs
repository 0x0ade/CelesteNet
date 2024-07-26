using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Server.Pico8;

public class Pico8Module : CelesteNetServerModule {
    public void Handle(CelesteNetConnection con, DataPicoState picoState) {
        if (picoState.Player == null) { return; }
        Server.BroadcastAsync(picoState);
    }

    public void Handle(CelesteNetConnection con, DataPicoEnd picoEnd) {
        if (picoEnd.Player == null) { return; }
        Server.BroadcastAsync(picoEnd);
    }
}