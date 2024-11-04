using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class FrontendSettings : CelesteNetServerModuleSettings {

        // Make sure to update your index.html as well!
        public string CanonicalRoot { get; set; } = "https://celestenet.0x0ade.ga/";

        [YamlIgnore]
        public string CanonicalAPIRoot => $"{CanonicalRoot.Substring(0, CanonicalRoot.Length - 1)}{APIPrefix}";

        public int Port { get; set; } = 17232;
        public string Password { get; set; } = "actuallyHosts";
        public string PasswordExec { get; set; } = "replaceThisASAP";

        public string ContentRoot { get; set; } = "Content";

        public string APIPrefix { get; set; } = "/api";

        public float NetPlusStatsUpdateRate { get; set; } = 1000;

        // TODO: Separate Discord auth module!
        [YamlIgnore]
        public string DiscordOAuthURL => $"https://discord.com/oauth2/authorize?client_id={DiscordOAuthClientID}&redirect_uri={Uri.EscapeDataString(DiscordOAuthRedirectURL)}&response_type=code&scope=identify&state=discord";
        [YamlIgnore]
        public string DiscordOAuthRedirectURL => $"{CanonicalAPIRoot}/standardauth";
        public string DiscordOAuthClientID { get; set; } = "";
        public string DiscordOAuthClientSecret { get; set; } = "";

        [YamlIgnore]
        public string TwitchOAuthURL => $"https://id.twitch.tv/oauth2/authorize?response_type=code&client_id={TwitchOAuthClientID}&redirect_uri={Uri.EscapeDataString(TwitchOAuthRedirectURL)}&response_type=code&state=twitch";
        [YamlIgnore]
        public string TwitchOAuthRedirectURL => $"{CanonicalAPIRoot}/standardauth";
        public string TwitchOAuthClientID { get; set; } = "";
        public string TwitchOAuthClientSecret { get; set; } = "";

        public HashSet<string> ExecOnlySettings { get; set; } = new();

    }
}
