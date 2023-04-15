using Microsoft.Xna.Framework;
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
        public string DiscordOAuthURL => $"https://discord.com/oauth2/authorize?client_id={DiscordOAuthClientID}&redirect_uri={Uri.EscapeDataString(DiscordOAuthRedirectURL)}&response_type=code&scope=identify";
        [YamlIgnore]
        public string DiscordOAuthRedirectURL => $"{CanonicalAPIRoot}/discordauth";
        public string DiscordOAuthClientID { get; set; } = "";
        public string DiscordOAuthClientSecret { get; set; } = "";

        public HashSet<string> ExecOnlySettings { get; set; } = new();

    }
}
