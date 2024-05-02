using System.Collections.Generic;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdW : CmdWhisper {

        public override string Info => $"Alias for {Chat.Commands.Get<CmdWhisper>().InvokeString}";

    }

    public class CmdWhisper : ChatCmd {

        public override CompletionType Completion => CompletionType.Player;

        public override string Info => "Send a whisper to someone else or toggle whispers.";

        public override string Help =>
$@"Send a whisper to someone else or toggle whispers.
To send a whisper to someone, {InvokeString} user text
To enable / disable whispers being sent to you, {InvokeString}";

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.IgnoreExtra = false;
            ArgParsers.Add(parser);

            parser = new(chat, this);
            parser.AddParameter(new ParamPlayerSession(chat));
            parser.AddParameter(new ParamString(chat), "message", "Psst, secret message...");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg>? args) {
            Logger.Log(LogLevel.DEV, "whisper", $"Run with '{args?.Count}' arguments: {args}");

            if (args == null || args.Count == 0) {
                CelesteNetPlayerSession? session = env.Session;
                if (session == null)
                    return;

                if (env.Server.UserData.GetKey(session.UID).IsNullOrEmpty())
                    throw new CommandRunException("You must be registered to enable / disable whispers!");

                ChatModule.UserChatSettings settings = env.Server.UserData.Load<ChatModule.UserChatSettings>(session.UID);
                settings.Whispers = !settings.Whispers;
                env.Server.UserData.Save(session.UID, settings);
                env.Send($"{(settings.Whispers ? "Enabled" : "Disabled")} whispers.");
                return;
            }

            if (args.Count < 2 || args[1] is not CmdArgString argMsg)
                throw new CommandRunException("No text.");

            if (args[0] is not CmdArgPlayerSession sessionArg) {
                throw new CommandRunException("Invalid username or ID.");
            }

            CelesteNetPlayerSession? other = sessionArg.Session;
            DataPlayerInfo otherPlayer = other?.PlayerInfo ?? throw new CommandRunException("Invalid username or ID.");

            if (!env.Server.UserData.Load<ChatModule.UserChatSettings>(other.UID).Whispers)
                throw new CommandRunException($"{otherPlayer.FullName} has blocked whispers.");

            DataPlayerInfo? player = env.Player;
            if (player != null) {
                env.Msg.Tag = $"whisper @ {otherPlayer.FullName}";
                env.Msg.Text = argMsg;
                env.Msg.Color = Chat.Settings.ColorWhisper;
                Chat.ForceSend(env.Msg);
                // remember the last whisper recipient/sender's session
                // env.Player is not null, therefore env.Session must not be null
                env.Session!.LastWhisperSessionID = other.SessionID;
                other.LastWhisperSessionID = env.Session!.SessionID;
            }

            other.Con.Send(Chat.PrepareAndLog(null, new DataChat {
                Player = player,
                Targets = [otherPlayer],
                Tag = "whisper",
                Text = argMsg,
                Color = Chat.Settings.ColorWhisper
            }));
        }

    }
}
