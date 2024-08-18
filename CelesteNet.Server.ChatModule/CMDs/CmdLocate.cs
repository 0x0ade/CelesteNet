using System;
using System.Collections.Generic;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd;

public class CmdLocate : ChatCmd {

    public override CompletionType Completion => CompletionType.Player;

    public override string Info => "Find where a player is.";

    public override CelesteNetSupportedClientFeatures RequiredFeatures => 
        CelesteNetSupportedClientFeatures.LocateCommand;

    private CelesteNetPlayerSession? Other;
    private DataPlayerInfo? OtherPlayer;

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
    }


    public override void Run(CmdEnv env, List<ICmdArg>? args) {
        if (Other == null || OtherPlayer == null)
            throw new InvalidOperationException("This should never happen if ValidatePlayerSession returns without error.");

        CelesteNetPlayerSession? self = env.Session ?? throw new CommandRunException("Cannot locate as the server.");

        var chat = new DataChat {
            Player = OtherPlayer,
            Tag = "locate",
            // On older clients, we never get here. On newer clients, this is replaced.
            Text = "{YOU SHOULD NEVER SEE THIS, PLEASE REPORT}",
            Color = Chat.Settings.ColorCommandReply
        };
        self.Con.Send(chat);
    }

}
