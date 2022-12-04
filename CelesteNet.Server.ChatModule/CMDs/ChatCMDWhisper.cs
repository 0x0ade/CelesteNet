using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatCMDW : ChatCMDWhisper {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<ChatCMDWhisper>().ID}";

    }

    public class ChatCMDWhisper : ChatCMD {

        public override string Args => "<user> <text>";

        public override string Info => "Send a whisper to someone else or toggle whispers.";

        public override string Help =>
$@"Send a whisper to someone else or toggle whispers.
To send a whisper to someone, {Chat.Settings.CommandPrefix}{ID} user text
To enable / disable whispers being sent to you, {Chat.Settings.CommandPrefix}{ID}";

        public override void Run(ChatCMDEnv env, List<ChatCMDArg> args) {
            if (args.Count == 0) {
                CelesteNetPlayerSession? session = env.Session;
                if (session == null)
                    return;

                if (env.Server.UserData.GetKey(session.UID).IsNullOrEmpty())
                    throw new Exception("You must be registered to enable / disable whispers!");

                ChatModule.UserChatSettings settings = env.Server.UserData.Load<ChatModule.UserChatSettings>(session.UID);
                settings.Whispers = !settings.Whispers;
                env.Server.UserData.Save(session.UID, settings);
                env.Send($"{(settings.Whispers ? "Enabled" : "Disabled")} whispers.");
                return;
            }

            if (args.Count == 1)
                throw new Exception("No text.");

            CelesteNetPlayerSession? other = args[0].Session;
            DataPlayerInfo otherPlayer = other?.PlayerInfo ?? throw new Exception("Invalid username or ID.");

            if (!env.Server.UserData.Load<ChatModule.UserChatSettings>(other.UID).Whispers)
                throw new Exception($"{otherPlayer.DisplayName} has blocked whispers.");

            string text = args[1].Rest;

            DataPlayerInfo? player = env.Player;
            if (player != null) {
                env.Msg.Tag = $"whisper @ {otherPlayer.DisplayName}";
                env.Msg.Text = text;
                env.Msg.Color = Chat.Settings.ColorWhisper;
                Chat.ForceSend(env.Msg);
                // remember the last session whisper
                // env.Player is not null, therefore env.Session must not be null
                env.Session!.LastWhisperSessionID = other.SessionID;
                other.LastWhisperSessionID = env.Session!.SessionID;
            }

            other.Con.Send(Chat.PrepareAndLog(null, new DataChat {
                Player = player,
                Target = otherPlayer,
                Tag = "whisper",
                Text = text,
                Color = Chat.Settings.ColorWhisper
            }));
        }

    }
}
