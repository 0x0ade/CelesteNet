using System;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Server.Pico8;
 
public class PicoGhost {

}

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

    private uint? GetIDOfConnection(CelesteNetConnection con) {
        CelesteNetPlayerSession? session = null;
        if (con != null)
            Server.PlayersByCon.TryGetValue(con, out session);
        return session?.PlayerInfo?.ID;
    }

    public void Handle(CelesteNetConnection con, DataPicoState picoState) {
        uint? id = GetIDOfConnection(con);
        if (id == null) { 
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "No ID found for state packet!");
            return;
        }
       
        //Logger.Log(LogLevel.INF, "PICO8-CNET", $"Broadcasting state packet for {id}...");

        picoState.ID = (uint) id;
        Server.BroadcastAsync(picoState);
    }

    public void Handle(CelesteNetConnection con, DataPicoEnd picoEnd) {
        uint? id = GetIDOfConnection(con);
        if (id == null) { 
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "No ID found for end packet!");
            return;
        }
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"Broadcasting end packet for {id}...");

        picoEnd.ID = (uint) id;
        Server.BroadcastAsync(picoEnd);
    }

    public void OnSessionEnd(CelesteNetPlayerSession session, DataPlayerInfo? info) {
        uint? id = info?.ID;
        if (id == null) { return; }

        Logger.Log(LogLevel.INF, "PICO8-CNET", $"Connection severed, broadcasting end packet for {id}...");
        Server.BroadcastAsync(new DataPicoEnd { ID = (uint) id });
    }
}