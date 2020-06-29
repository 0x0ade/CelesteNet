using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Newtonsoft.Json;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

#if NETCORE
using System.Runtime.Loader;
#endif

namespace Celeste.Mod.CelesteNet.Server.Control {
    public static partial class RCEndpoints {

        public static readonly string COOKIE_DISCORDAUTH = "celestenet-discordauth";
        public static readonly string COOKIE_KEY = "celestenet-key";

        [RCEndpoint(false, "/discordauth", "", "", "Discord OAuth2", "User auth using Discord.")]
        public static void DiscordOAuth(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            if (args.Count == 0) {
                // c.Response.Redirect(f.Settings.OAuthURL);
                c.Response.StatusCode = (int) HttpStatusCode.Redirect;
                c.Response.Headers.Set("Location", f.Settings.DiscordOAuthURL);
                f.RespondJSON(c, new {
                    Info = $"Redirecting to {f.Settings.DiscordOAuthURL}"
                });
                return;
            }

            string code = args["code"];
            if (string.IsNullOrEmpty(code)) {
                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                f.RespondJSON(c, new {
                    Error = "No code specified."
                });
                return;
            }

            dynamic? tokenData;
            dynamic? userData;

            using (HttpClient client = new HttpClient()) {

                using (Stream s = client.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>() {
                    { "client_id", f.Settings.DiscordOAuthClientID },
                    { "client_secret", f.Settings.DiscordOAuthClientSecret },
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "redirect_uri", f.Settings.DiscordOAuthRedirectURL },
                    { "scope", "identity" }
                })).Await().Content.ReadAsStreamAsync().Await())
                using (StreamReader sr = new StreamReader(s))
                using (JsonTextReader jtr = new JsonTextReader(sr))
                    tokenData = f.Serializer.Deserialize<dynamic>(jtr);

                if (!(tokenData?.access_token?.ToString() is string token) ||
                    !(tokenData?.token_type?.ToString() is string tokenType) ||
                    string.IsNullOrEmpty(token) ||
                    string.IsNullOrEmpty(tokenType)) {
                    Logger.Log(LogLevel.CRI, "frontend-discordauth", $"Failed to obtain token: {tokenData}");
                    c.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                    f.RespondJSON(c, new {
                        Error = "Couldn't obtain access token from Discord."
                    });
                    return;
                }


                using (Stream s = client.SendAsync(new HttpRequestMessage {
                    RequestUri = new Uri("https://discord.com/api/users/@me"),
                    Method = HttpMethod.Get,
                    Headers = {
                        { "Authorization", $"{tokenType} {token}" }
                    }
                }).Await().Content.ReadAsStreamAsync().Await())
                using (StreamReader sr = new StreamReader(s))
                using (JsonTextReader jtr = new JsonTextReader(sr))
                    userData = f.Serializer.Deserialize<dynamic>(jtr);
            }

            if (!(userData?.id?.ToString() is string uid) ||
                string.IsNullOrEmpty(uid)) {
                Logger.Log(LogLevel.CRI, "frontend-discordauth", $"Failed to obtain ID: {userData}");
                c.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                f.RespondJSON(c, new {
                    Error = "Couldn't obtain user ID from Discord."
                });
                return;
            }

            string key = f.Server.UserData.Create(uid);
            BasicUserInfo info = f.Server.UserData.Load<BasicUserInfo>(uid);
            info.Name =  userData.username.ToString();
            info.Discrim =  userData.discriminator.ToString();
            info.Avatar =  userData.avatar.ToString();
            f.Server.UserData.Save(uid, info);

            c.Response.StatusCode = (int) HttpStatusCode.Redirect;
            c.Response.Headers.Set("Location", $"http://{c.Request.UserHostName}/");
            c.Response.SetCookie(new Cookie(COOKIE_KEY, key));
            c.Response.SetCookie(new Cookie(COOKIE_DISCORDAUTH, code));
            f.RespondJSON(c, new {
                Info = "Success - redirecting to /"
            });
        }

        [RCEndpoint(false, "/userinfo", "?uid={uid}&key={keyIfNoUID}", "", "User Info", "Get some basic user info.")]
        public static void UserInfo(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);

            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            string uid = args["uid"];
            if (string.IsNullOrEmpty(uid)) {
                string key = args["key"];
                if (string.IsNullOrEmpty(key))
                    key = c.Request.Cookies[COOKIE_KEY]?.Value ?? "";
                if (string.IsNullOrEmpty(key)) {
                    c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                    f.RespondJSON(c, new {
                        Error = "Unauthorized - no key or uid."
                    });
                    return;
                }

                uid = f.Server.UserData.GetUID(key);
                if (string.IsNullOrEmpty(uid)) {
                    c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                    f.RespondJSON(c, new {
                        Error = "Unauthorized - invalid key."
                    });
                }

                auth = true;
            }

            if (string.IsNullOrEmpty(uid)) {
                c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                f.RespondJSON(c, new {
                    Error = "Unauthorized - invalid uid."
                });
                return;
            }

            BasicUserInfo info = f.Server.UserData.Load<BasicUserInfo>(uid);

            f.RespondJSON(c, new {
                info.Name,
                info.Discrim,
                info.Avatar,
                info.Tags,
                Key = auth ? f.Server.UserData.GetKey(uid) : null
            });
        }

    }
}
