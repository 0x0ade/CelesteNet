using Celeste.Mod.CelesteNet.DataTypes;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class CmdE : CmdEmote {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<CmdEmote>().ID}";

    }

    public class CmdEmote : ChatCmd {

        public override CompletionType Completion => CompletionType.Emote;

        public override string Info => "Send an emote appearing over your player.";
        public override string Help =>
@"Send an emote appearing over your player.
Normal text appears over your player.
This syntax also works for your ""favorites"" (settings file).
i:TEXTURE shows TEXTURE from the GUI atlas.
p:TEXTURE shows TEXTURE from the Portraits atlas.
g:TEXTURE shows TEXTURE from the Gameplay atlas.
p:FRM1 FRM2 FRM3 plays an animation, 7 FPS by default.
p:10 FRM1 FRM2 FRM3 plays the animation at 10 FPS.";

        public override void Init(ChatModule chat) {
            Chat = chat;

            ArgParser parser = new(chat, this);
            parser.AddParameter(new ParamString(chat), "<text> | i:<img> | p:<img> | g:<img>", "p: madeline/normal");
            ArgParsers.Add(parser);
        }

        public override void Run(CmdEnv env, List<ICmdArg>? args) {
            if (env.Session == null)
                return;

            if (args == null || args.Count == 0 || args[0] is not CmdArgString argEmote || string.IsNullOrWhiteSpace(argEmote)) {
                throw new CommandRunException("No emote argument given.");
            }

            DataEmote emote = new() {
                Player = env.Player,
                Text = argEmote.String.Trim()
            };
            env.Session.Con.Send(emote);
            env.Server.Data.Handle(env.Session.Con, emote);
        }

    }
}
