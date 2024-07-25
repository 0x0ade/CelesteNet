using System.Collections.Generic;
using System.IO;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Server.Pico8;
 
public class PicoGhost {

}

public class Pico8Module : CelesteNetServerModule<Pico8Settings> {

    public override void Init(CelesteNetServerModuleWrapper wrapper) {
        base.Init(wrapper);

        using (Server.ConLock.R())
            foreach (CelesteNetPlayerSession session in Server.Sessions)
                session.OnEnd += OnSessionEnd;
    }

    public void Handle(CelesteNetConnection con, DataPicoCreate picoCreate) {
        Server.BroadcastAsync(picoCreate);
    }


    public void Handle(CelesteNetConnection con, DataPicoState picoState) {
        Server.BroadcastAsync(picoState);
    }

    public void Handle(CelesteNetConnection con, DataPicoEnd picoEnd) {
        Server.BroadcastAsync(picoEnd);
    }

    public void OnSessionEnd(CelesteNetPlayerSession session, DataPlayerInfo? info) {
        Server.BroadcastAsync(new DataPicoEnd { ID = info?.ID ?? (uint.MaxValue - 1) });
    }
}