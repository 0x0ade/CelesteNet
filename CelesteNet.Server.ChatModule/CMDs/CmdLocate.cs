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
            // TODO: This really, REALLY should be a handshake.
            Text = "Your client is out of date and does not support /locate. Please update and try again.",
            Color = Chat.Settings.ColorCommandReply
        };
        self.Con.Send(Chat.PrepareAndLog(self, chat));
    }

}
