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

        public class OAuthProvider {
            public string OAuthPathAuthorize { get; set; } = "";
            public string OAuthPathToken { get; set; } = "";
            public string OAuthScope { get; set; } = "identify";
            public string OAuthClientID { get; set; } = "";
            public string OAuthClientSecret { get; set; } = "";

            public string ServiceUserAPI { get; set; } = "";

            public string ServiceUserJsonPathUid { get; set; } = "$.id";

            public string ServiceUserJsonPathName { get; set; } = "$.username";

            public string ServiceUserJsonPathPfp { get; set; } = "$.avatar";

            // will be put through string.Format with {0} = uid (ServiceUserJsonPathUid) and {1} = pfpFragment (ServiceUserJsonPathPfp)
            public string ServiceUserAvatarURL { get; set; } = "";

            public string ServiceUserAvatarDefaultURL { get; set; } = "";

            public string OAuthURL(string redirectURL, string state) {
                return $"{OAuthPathAuthorize}?client_id={OAuthClientID}&redirect_uri={Uri.EscapeDataString(redirectURL)}&response_type=code&scope={OAuthScope}&state={Uri.EscapeDataString(state)}";
            }
        }

        public Dictionary<string,OAuthProvider> OAuthProviders { get; set; } = 
            new Dictionary<string, OAuthProvider>() {
                { "discord",
                  new OAuthProvider()
                  {
                      OAuthPathAuthorize = "https://discord.com/oauth2/authorize",
                      OAuthPathToken = "https://discord.com/api/oauth2/token",
                      ServiceUserAPI = "https://discord.com/api/users/@me",
                      ServiceUserJsonPathUid = "$.id",
                      ServiceUserJsonPathName = "$.['global_name', 'username']",
                      ServiceUserJsonPathPfp = "$.avatar",
                      ServiceUserAvatarURL = "https://cdn.discordapp.com/avatars/{0}/{1}.png?size=64",
                      ServiceUserAvatarDefaultURL = "https://cdn.discordapp.com/embed/avatars/0.png"
                  }
                }
            };

        [YamlIgnore]
        public string OAuthRedirectURL => $"{CanonicalAPIRoot}/oauth";

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
