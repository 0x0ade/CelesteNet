using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Server.Pico8;

public class Pico8Module : CelesteNetServerModule {

    public override void Init(CelesteNetServerModuleWrapper wrapper) {
        base.Init(wrapper);

        Server.OnSessionStart += OnSessionStart;

        using (Server.ConLock.R())
            foreach (CelesteNetPlayerSession session in Server.Sessions)
                session.OnEnd += OnSessionEnd;
    }

    private void OnSessionStart(CelesteNetPlayerSession session)
    {
        session.OnEnd += OnSessionEnd;
    }

    public void Handle(CelesteNetConnection con, DataPicoState picoState) {
        if (picoState.Player == null) { return; }
        Server.BroadcastAsync(picoState);
    }

    public void Handle(CelesteNetConnection con, DataPicoEnd picoEnd) {
        if (picoEnd.Player == null) { return; }
        Server.BroadcastAsync(picoEnd);
    }

    public void OnSessionEnd(CelesteNetPlayerSession session, DataPlayerInfo? info) {
        Server.BroadcastAsync(new DataPicoEnd { Player = info });
    }
}