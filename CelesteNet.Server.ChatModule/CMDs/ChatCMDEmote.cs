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
    public class ChatCMDE : ChatCMDEmote {

        public override string Info => $"Alias for {Chat.Settings.CommandPrefix}{Chat.Commands.Get<ChatCMDEmote>().ID}";

    }

    public class ChatCMDEmote : ChatCMD {

        public override string Args => "<text> | i:<img> | p:<img> | g:<img>";

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

        public override void ParseAndRun(ChatCMDEnv env) {
            if (env.Session == null || string.IsNullOrWhiteSpace(env.Text))
                return;

            DataEmote emote = new() {
                Player = env.Player,
                Text = env.Text.Trim()
            };
            env.Session.Con.Send(emote);
            env.Server.Data.Handle(env.Session.Con, emote);
        }

    }
}
