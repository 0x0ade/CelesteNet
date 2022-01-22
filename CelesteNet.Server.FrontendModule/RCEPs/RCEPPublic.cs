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
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

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

            if (args["error"] == "access_denied") {
                c.Response.StatusCode = (int) HttpStatusCode.Redirect;
                c.Response.Headers.Set("Location", $"http://{c.Request.UserHostName}/");
                c.Response.SetCookie(new(COOKIE_KEY, ""));
                c.Response.SetCookie(new(COOKIE_DISCORDAUTH, ""));
                f.RespondJSON(c, new {
                    Info = "Denied - redirecting to /"
                });
                return;
            }

            string? code = args["code"];
            if (code.IsNullOrEmpty()) {
                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                f.RespondJSON(c, new {
                    Error = "No code specified."
                });
                return;
            }

            dynamic? tokenData;
            dynamic? userData;

            using (HttpClient client = new()) {
#pragma warning disable CS8714 // new FormUrlEncodedContent expects nullable.
                using (Stream s = client.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(new Dictionary<string?, string?>() {
#pragma warning restore CS8714
                    { "client_id", f.Settings.DiscordOAuthClientID },
                    { "client_secret", f.Settings.DiscordOAuthClientSecret },
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "redirect_uri", f.Settings.DiscordOAuthRedirectURL },
                    { "scope", "identity" }
                })).Await().Content.ReadAsStreamAsync().Await())
                using (StreamReader sr = new(s))
                using (JsonTextReader jtr = new(sr))
                    tokenData = f.Serializer.Deserialize<dynamic>(jtr);

                if (!(tokenData?.access_token?.ToString() is string token) ||
                    !(tokenData?.token_type?.ToString() is string tokenType) ||
                    token.IsNullOrEmpty() ||
                    tokenType.IsNullOrEmpty()) {
                    Logger.Log(LogLevel.CRI, "frontend-discordauth", $"Failed to obtain token: {tokenData}");
                    c.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                    f.RespondJSON(c, new {
                        Error = "Couldn't obtain access token from Discord."
                    });
                    return;
                }


                using (Stream s = client.SendAsync(new HttpRequestMessage {
                    RequestUri = new("https://discord.com/api/users/@me"),
                    Method = HttpMethod.Get,
                    Headers = {
                        { "Authorization", $"{tokenType} {token}" }
                    }
                }).Await().Content.ReadAsStreamAsync().Await())
                using (StreamReader sr = new(s))
                using (JsonTextReader jtr = new(sr))
                    userData = f.Serializer.Deserialize<dynamic>(jtr);
            }

            if (!(userData?.id?.ToString() is string uid) ||
                uid.IsNullOrEmpty()) {
                Logger.Log(LogLevel.CRI, "frontend-discordauth", $"Failed to obtain ID: {userData}");
                c.Response.StatusCode = (int) HttpStatusCode.InternalServerError;
                f.RespondJSON(c, new {
                    Error = "Couldn't obtain user ID from Discord."
                });
                return;
            }

            string key = f.Server.UserData.Create(uid, false);
            BasicUserInfo info = f.Server.UserData.Load<BasicUserInfo>(uid);
            info.Name = userData.username.ToString();
            info.Discrim = userData.discriminator.ToString();
            f.Server.UserData.Save(uid, info);

            Image avatarOrig;
            using (HttpClient client = new()) {
                try {
                    using Stream s = client.GetAsync(
                        $"https://cdn.discordapp.com/avatars/{uid}/{userData.avatar.ToString()}.png?size=64"
                    ).Await().Content.ReadAsStreamAsync().Await();
                    avatarOrig = Image.FromStream(s);
                } catch {
                    using Stream s = client.GetAsync(
                        $"https://cdn.discordapp.com/embed/avatars/{((int) userData.discriminator) % 6}.png"
                    ).Await().Content.ReadAsStreamAsync().Await();
                    avatarOrig = Image.FromStream(s);
                }
            }
            using (avatarOrig)
            using (Bitmap avatarScale = new(64, 64, PixelFormat.Format32bppArgb))
            using (Bitmap avatarFinal = new(64, 64, PixelFormat.Format32bppArgb)) {
                using (Graphics g = Graphics.FromImage(avatarScale)) {
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    g.DrawImage(avatarOrig, 0, 0, 64, 64);
                }

                using (Stream s = f.Server.UserData.WriteFile(uid, "avatar.orig.png"))
                    avatarScale.Save(s, ImageFormat.Png);

                using (Graphics g = Graphics.FromImage(avatarFinal)) {
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    using (TextureBrush tbr = new(avatarScale))
                        g.FillEllipse(tbr, 0, 0, 64, 64);

                    foreach (string tagName in info.Tags) {
                        using Stream? asset = f.OpenContent($"frontend/assets/tags/{tagName}.png", out _, out _, out _);
                        if (asset == null)
                            continue;

                        using Image tag = Image.FromStream(asset);
                        g.DrawImageUnscaled(tag, 0, 0);
                    }
                }

                using (Stream s = f.Server.UserData.WriteFile(uid, "avatar.png"))
                    avatarFinal.Save(s, ImageFormat.Png);
            }

            c.Response.StatusCode = (int) HttpStatusCode.Redirect;
            c.Response.Headers.Set("Location", $"http://{c.Request.UserHostName}/");
            c.Response.SetCookie(new(COOKIE_KEY, key));
            c.Response.SetCookie(new(COOKIE_DISCORDAUTH, code));
            f.RespondJSON(c, new {
                Info = "Success - redirecting to /"
            });
        }

        [RCEndpoint(false, "/userinfo", "?uid={uid}&key={keyIfNoUID}", "", "User Info", "Get some basic user info.")]
        public static void UserInfo(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);

            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            string? uid = args["uid"];
            if (uid.IsNullOrEmpty()) {
                string? key = args["key"];
                if (key.IsNullOrEmpty())
                    key = c.Request.Cookies[COOKIE_KEY]?.Value ?? "";
                if (key.IsNullOrEmpty()) {
                    c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                    f.RespondJSON(c, new {
                        Error = "Unauthorized - no key or uid."
                    });
                    return;
                }

                uid = f.Server.UserData.GetUID(key);
                if (uid.IsNullOrEmpty()) {
                    c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                    f.RespondJSON(c, new {
                        Error = "Unauthorized - invalid key."
                    });
                    return;
                }

                auth = true;
            }

            if (uid.IsNullOrEmpty()) {
                c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                f.RespondJSON(c, new {
                    Error = "Unauthorized - invalid uid."
                });
                return;
            }

            BasicUserInfo info = f.Server.UserData.Load<BasicUserInfo>(uid);

            f.RespondJSON(c, new {
                UID = uid,
                info.Name,
                info.Discrim,
                info.Tags,
                Key = auth ? f.Server.UserData.GetKey(uid) : null
            });
        }

        [RCEndpoint(false, "/revokekey", "?key={key}", "", "Revoke Key", "Revoke the given key.")]
        public static void RevokeKey(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            string? key = args["key"];
            if (key.IsNullOrEmpty())
                key = c.Request.Cookies[COOKIE_KEY]?.Value ?? "";
            if (key.IsNullOrEmpty()) {
                c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                f.RespondJSON(c, new {
                    Error = "Unauthorized - no key."
                });
                return;
            }

            string uid = f.Server.UserData.GetUID(key);
            if (uid.IsNullOrEmpty()) {
                c.Response.StatusCode = (int) HttpStatusCode.Unauthorized;
                f.RespondJSON(c, new {
                    Error = "Unauthorized - invalid key."
                });
            }

            f.Server.UserData.RevokeKey(key);

            f.RespondJSON(c, new {
                Info = "Key revoked."
            });
        }

        [RCEndpoint(false, "/avatar", "?uid={uid}", "", "Get Avatar", "Get a 64x64 round user avatar PNG.")]
        public static void Avatar(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            string? uid = args["uid"];
            if (uid.IsNullOrEmpty()) {
                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                f.RespondJSON(c, new {
                    Error = "No UID."
                });
                return;
            }

            Stream? data = f.Server.UserData.ReadFile(uid, "avatar.png");
            if (data == null) {
                c.Response.StatusCode = (int) HttpStatusCode.NotFound;
                f.RespondJSON(c, new {
                    Error = "Not found."
                });
                return;
            }

            c.Response.ContentType = "image/png";
            f.RespondContent(c, data);
        }

    }
}
