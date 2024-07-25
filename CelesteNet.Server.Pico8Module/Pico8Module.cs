﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;

using Exception = System.Exception;

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

    private uint? GetIDOfConnection(CelesteNetConnection con) {
        CelesteNetPlayerSession? session = null;
        if (con != null)
            Server.PlayersByCon.TryGetValue(con, out session);
        return session?.PlayerInfo?.ID;
    }

    public void Handle(CelesteNetConnection con, DataPicoCreate picoCreate) {
        uint? id = GetIDOfConnection(con);
        if (id == null) {
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "No ID found for create packet!");
            return;
        }
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"Broadcasting create packet for {id}...");

        picoCreate.ID = (uint) id;
        Server.BroadcastAsync(picoCreate);
    }


    public void Handle(CelesteNetConnection con, DataPicoState picoState) {
        uint? id = GetIDOfConnection(con);
        if (id == null) { 
            Logger.Log(LogLevel.WRN, "PICO8-CNET", "No ID found for state packet!");
            return;
        }
       
        Logger.Log(LogLevel.INF, "PICO8-CNET", $"Broadcasting state packet for {id}...");

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
        uint? id = session?.PlayerInfo?.ID;
        if (id == null) { return; }

        Server.BroadcastAsync(new DataPicoEnd { ID = (uint) id });
    }
}