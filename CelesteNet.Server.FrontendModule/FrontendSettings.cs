﻿using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using YamlDotNet.Serialization;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class FrontendSettings : CelesteNetServerModuleSettings {

        // Make sure to update your index.html as well!
        public string CanonicalRoot { get; set; } = "https://celestenet.0x0ade.ga/";

        public int Port { get; set; } = 3800;
        public string Password { get; set; } = "actuallyHosts";
        public string PasswordExec { get; set; } = "replaceThisASAP";

        public string ContentRoot { get; set; } = "Content";

        public float NetPlusStatsUpdateRate { get; set; } = 1000;

        // TODO: Separate Discord auth module!
        [YamlIgnore]
        public string DiscordOAuthURL => $"https://discord.com/oauth2/authorize?client_id={DiscordOAuthClientID}&redirect_uri={Uri.EscapeDataString(DiscordOAuthRedirectURL)}&response_type=code&scope=identify";
        [YamlIgnore]
        public string DiscordOAuthRedirectURL => $"{CanonicalRoot}discordauth";
        public string DiscordOAuthClientID { get; set; } = "";
        public string DiscordOAuthClientSecret { get; set; } = "";

    }
}
