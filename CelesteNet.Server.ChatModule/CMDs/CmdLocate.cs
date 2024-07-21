using System;
using System.Collections.Generic;
using Celeste.Mod.CelesteNet.DataTypes;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd;

public class CmdLocate : ChatCmd {

    public override CompletionType Completion => CompletionType.Player;

    public override string Info => "Find where a player is.";

    private CelesteNetPlayerSession? Other;
    private DataPlayerInfo? OtherPlayer;
    private DataPlayerState? OtherState;

    public override void Init(ChatModule chat) {
        Chat = chat;

        ArgParser parser = new(chat, this);
        parser.AddParameter(new ParamPlayerSession(chat, ValidatePlayerSession));
        ArgParsers.Add(parser);
    }

    private void ValidatePlayerSession(string raw, CmdEnv env, ICmdArg arg) {
        if (arg is not CmdArgPlayerSession sessionArg)
            throw new CommandRunException("Invalid username or ID.");

        CelesteNetPlayerSession? other = sessionArg.Session;
        DataPlayerInfo otherPlayer = other?.PlayerInfo ?? throw new CommandRunException("Invalid username or ID.");

        Other = other;
        OtherPlayer = otherPlayer;

        if (!env.Server.Data.TryGetBoundRef(otherPlayer, out DataPlayerState? otherState) ||
            otherState == null ||
            otherState.SID.IsNullOrEmpty())
            return;

        OtherState = otherState;
    }


    public override void Run(CmdEnv env, List<ICmdArg>? args) {
        if (Other == null || OtherPlayer == null)
            throw new InvalidOperationException("This should never happen if ValidatePlayerSession returns without error.");

        if (OtherState == null) {
            env.Send($"{OtherPlayer.FullName} isn't in game.");
            return;
        }

        // TODO:
        //    Ideally, part of CelesteNetPlayerListComponent should be factored out for this.
        //    I don't have the time or energy to *do* that, though.

        string chapter = OtherState.SID;
        string Side = ((char) ('A' + (int) OtherState.Mode)).ToString();
        string Level = OtherState.Level;

        env.Send($"{OtherPlayer.FullName} is in room {Level} of {chapter}, side {Side}.");
    }

}
