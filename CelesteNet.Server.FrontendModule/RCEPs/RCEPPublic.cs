using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using Celeste.Mod.CelesteNet.Server.Chat;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public static partial class RCEndpoints {


        [RCEndpoint(false, "/twitchauth", "", "", "Twitch OAuth2", "User auth using Twitch.")]
        public static void TwitchOAuth(Frontend f, HttpRequestEventArgs c)
        {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);
            Logger.Log(LogLevel.DBG, "frontend-twitchauth", $"{c.Request.RawUrl}");
            Logger.Log(LogLevel.DBG, "frontend-twitchauth", $"{f.ParseQueryString(c.Request.RawUrl)}");
            if (args.Count == 0)
            {
                // c.Response.Redirect(f.Settings.OAuthURL);
                c.Response.StatusCode = (int)HttpStatusCode.Redirect;
                c.Response.Headers.Set("Location", f.Settings.TwitchOAuthURL);
                f.RespondJSON(c, new
                {
                    Info = $"Redirecting to {f.Settings.TwitchOAuthURL}"
                });
                
                return;
            }

            if (args["error"] == "access_denied")
            {
                c.Response.StatusCode = (int)HttpStatusCode.Redirect;
                c.Response.Headers.Set("Location", $"http://{c.Request.UserHostName}/");
                f.UnsetKeyCookie(c);
                f.UnsetDiscordAuthCookie(c);
                f.RespondJSON(c, new
                {
                    Info = "Denied - redirecting to /"
                });
                return;
            }

            string? code = args["code"];
            if (code.IsNullOrEmpty())
            {
                c.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                f.RespondJSON(c, new
                {
                    Error = "No code specified."
                });
                return;
            }

            dynamic? tokenData;
            dynamic? userData;

            using (HttpClient client = new())
            {
#pragma warning disable CS8714 // new FormUrlEncodedContent expects nullable.
                using (Stream s = client.PostAsync("https://id.twitch.tv/oauth2/token", new FormUrlEncodedContent(new Dictionary<string?, string?>() {
#pragma warning restore CS8714
                    { "client_id", f.Settings.TwitchOAuthClientID },
                    { "client_secret", f.Settings.TwitchOAuthClientSecret },
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "redirect_uri", f.Settings.TwitchOAuthRedirectURL },
                })).Await().Content.ReadAsStreamAsync().Await())
                using (StreamReader sr = new(s))
                using (JsonTextReader jtr = new(sr))
                    tokenData = f.Serializer.Deserialize<dynamic>(jtr);

                if (tokenData?.access_token?.ToString() is not string token ||
                    tokenData?.token_type?.ToString() is not string tokenType ||
                    token.IsNullOrEmpty() ||
                    tokenType.IsNullOrEmpty())
                {
                    Logger.Log(LogLevel.CRI, "frontend-twitchauth", $"Failed to obtain token: {tokenData}");
                    c.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    f.RespondJSON(c, new
                    {
                        Error = "Couldn't obtain access token from Twitch."
                    });
                    return;
                }

                if (tokenType == "bearer")
                    tokenType = "Bearer";

                using (Stream s = client.SendAsync(new HttpRequestMessage
                {
                    RequestUri = new("https://api.twitch.tv/helix/users"),
                    Method = HttpMethod.Get,
                    Headers = {
                        { "Authorization", $"{tokenType} {token}" },
                        { "Client-Id", f.Settings.TwitchOAuthClientID }
                    }
                }).Await().Content.ReadAsStreamAsync().Await())
                using (StreamReader sr = new(s))
                using (JsonTextReader jtr = new(sr))
                    userData = f.Serializer.Deserialize<dynamic>(jtr);
            }

            if (!($"{userData?.data[0].id}" is string uid) ||
                uid.IsNullOrEmpty())
            {
                Logger.Log(LogLevel.CRI, "frontend-twitchauth", $"Failed to obtain ID: {userData?.data[0].id}");
                c.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                f.RespondJSON(c, new
                {
                    Error = "Couldn't obtain user ID from Twitch."
                });
                return;
            }

            string key = f.Server.UserData.Create(uid, false);
            BasicUserInfo info = f.Server.UserData.Load<BasicUserInfo>(uid);

            if ($"{userData?.data[0].display_name}" is string global_name && !global_name.IsNullOrEmpty())
            {
                info.Name = global_name;
            }
            if (info.Name.Length > 32)
            {
                info.Name = info.Name.Substring(0, 32);
            }
            info.Discrim = "TWITCH";
            f.Server.UserData.Save(uid, info);

            Image avatarOrig;
            using (HttpClient client = new())
            {
                try
                {
                    using Stream s = client.GetAsync(
                        $"{userData?.data[0].profile_image_url}"
                    ).Await().Content.ReadAsStreamAsync().Await();
                    avatarOrig = Image.Load<Rgba32>(s);
                }
                catch
                {
                    using Stream s = client.GetAsync(
                        $"https://static-cdn.jtvnw.net/user-default-pictures-uv/13e5fa74-defa-11e9-809c-784f43822e80-profile_image-300x300.png"
                    ).Await().Content.ReadAsStreamAsync().Await();
                    avatarOrig = Image.Load(s);
                }
            }

            using (avatarOrig)
            using (Image avatarScale = avatarOrig.Clone(x => x.Resize(64, 64, sampler: KnownResamplers.Lanczos3)))
            using (Image avatarFinal = avatarScale.Clone(x => x.ApplyRoundedCorners().ApplyTagOverlays(f, info)))
            {

                using (Stream s = f.Server.UserData.WriteFile(uid, "avatar.orig.png"))
                    avatarScale.SaveAsPng(s, new PngEncoder() { ColorType = PngColorType.RgbWithAlpha });

                using (Stream s = f.Server.UserData.WriteFile(uid, "avatar.png"))
                    avatarFinal.SaveAsPng(s, new PngEncoder() { ColorType = PngColorType.RgbWithAlpha });
            }

            c.Response.StatusCode = (int)HttpStatusCode.Redirect;
            c.Response.Headers.Set("Location", $"http://{c.Request.UserHostName}/");
            f.SetKeyCookie(c, key);
            f.SetDiscordAuthCookie(c, code);
            f.RespondJSON(c, new
            {
                Info = "Success - redirecting to /"
            });
        }



        [RCEndpoint(false, "/discordauth", "", "", "Discord OAuth2", "User auth using Discord.")]
        public static void DiscordOAuth(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);
            Logger.Log(LogLevel.DBG, "frontend-discordauth", $"{c.Request.RawUrl}");
            Logger.Log(LogLevel.DBG, "frontend-discordauth", $"{f.ParseQueryString(c.Request.RawUrl)}");

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
                f.UnsetKeyCookie(c);
                f.UnsetDiscordAuthCookie(c);
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

                if (tokenData?.access_token?.ToString() is not string token ||
                    tokenData?.token_type?.ToString() is not string tokenType ||
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

            if (userData.global_name?.ToString() is string global_name && !global_name.IsNullOrEmpty()) {
                info.Name = global_name;
            } else {
                info.Name = userData.username.ToString();
            }
            if (info.Name.Length > 32) {
                info.Name = info.Name.Substring(0, 32);
            }
            info.Discrim = userData.discriminator.ToString();
            f.Server.UserData.Save(uid, info);

            Image avatarOrig;
            using (HttpClient client = new()) {
                try {
                    using Stream s = client.GetAsync(
                        $"https://cdn.discordapp.com/avatars/{uid}/{userData.avatar.ToString()}.png?size=64"
                    ).Await().Content.ReadAsStreamAsync().Await();
                    avatarOrig = Image.Load<Rgba32>(s);
                } catch {
                    using Stream s = client.GetAsync(
                        $"https://cdn.discordapp.com/embed/avatars/{((int) userData.discriminator) % 6}.png"
                    ).Await().Content.ReadAsStreamAsync().Await();
                    avatarOrig = Image.Load(s);
                }
            }

            using (avatarOrig)
            using (Image avatarScale = avatarOrig.Clone(x => x.Resize(64, 64, sampler: KnownResamplers.Lanczos3)))
            using (Image avatarFinal = avatarScale.Clone(x => x.ApplyRoundedCorners().ApplyTagOverlays(f, info))) {

                using (Stream s = f.Server.UserData.WriteFile(uid, "avatar.orig.png"))
                    avatarScale.SaveAsPng(s, new PngEncoder() { ColorType = PngColorType.RgbWithAlpha });

                using (Stream s = f.Server.UserData.WriteFile(uid, "avatar.png"))
                    avatarFinal.SaveAsPng(s, new PngEncoder() { ColorType = PngColorType.RgbWithAlpha });
            }

            c.Response.StatusCode = (int) HttpStatusCode.Redirect;
            c.Response.Headers.Set("Location", $"http://{c.Request.UserHostName}/");
            f.SetKeyCookie(c, key);
            f.SetDiscordAuthCookie(c, code);
            f.RespondJSON(c, new {
                Info = "Success - redirecting to /"
            });
        }

        private static IImageProcessingContext ApplyTagOverlays(this IImageProcessingContext context, Frontend f, BasicUserInfo info) {
            foreach (string tagName in info.Tags) {
                using Stream? asset = f.OpenContent($"frontend/assets/tags/{tagName}.png", out _, out _, out _);
                if (asset == null)
                    continue;

                using Image tag = Image.Load(asset);
                context.DrawImage(tag, 1.0f);
            }

            return context;
        }

        // These functions copied from ImageSharp's Samples code -- https://github.com/SixLabors/Samples/blob/main/ImageSharp/AvatarWithRoundedCorner/Program.cs

        // This method can be seen as an inline implementation of an `IImageProcessor`:
        // (The combination of `IImageOperations.Apply()` + this could be replaced with an `IImageProcessor`)
        private static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext context) {
            Size size = context.GetCurrentSize();

            GraphicsOptions oldGO = context.GetGraphicsOptions();

            context.SetGraphicsOptions(new GraphicsOptions() {
                Antialias = true,

                // Enforces that any part of this shape that has color is punched out of the background
                AlphaCompositionMode = PixelAlphaCompositionMode.DestOut
            });

            IPath rect = new RectangularPolygon(0, 0, size.Width, size.Height);
            rect = rect.Clip(new EllipsePolygon(size.Width / 2, size.Height / 2, size.Width / 2));

            // Mutating in here as we already have a cloned original
            // use any color (not Transparent), so the corners will be clipped
            context = context.Fill(Color.Red, rect);

            context.SetGraphicsOptions(oldGO);

            return context;
        }

        [RCEndpoint(false, "/userinfo", "?uid={uid}&key={keyIfNoUID}", "", "User Info", "Get some basic user info.")]
        public static void UserInfo(Frontend f, HttpRequestEventArgs c) {
            bool auth = f.IsAuthorized(c);
            bool usedOwnKey = false;

            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            string? uid = args["uid"];
            if (uid.IsNullOrEmpty()) {
                string? key = args["key"];
                if (key.IsNullOrEmpty())
                    key = f.TryGetKeyCookie(c) ?? "";
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
                usedOwnKey = true;
            }

            BasicUserInfo info = f.Server.UserData.Load<BasicUserInfo>(uid);

            // prevent leaking other moderator and admin keys when using a session
            if (!usedOwnKey && info.Tags.Intersect(BasicUserInfo.AUTH_TAGS).Any() && !f.IsAuthorizedExec(c))
                auth = false;

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
                key = f.TryGetKeyCookie(c) ?? "";
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
                return;
            }

            f.Server.UserData.RevokeKey(key);

            f.RespondJSON(c, new {
                Info = "Key revoked."
            });
        }

        [RCEndpoint(false, "/resetuserdata", "?key={key}", "", "Reset User Data", "Reset/remove extra bits of info stored about the user.")]
        public static void ResetUserData(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            string? key = args["key"];
            if (key.IsNullOrEmpty())
                key = f.TryGetKeyCookie(c) ?? "";
            if (key.IsNullOrEmpty()) {
                c.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                f.RespondJSON(c, new {
                    Error = "Unauthorized - no key."
                });
                return;
            }

            string uid = f.Server.UserData.GetUID(key);
            if (uid.IsNullOrEmpty()) {
                c.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                f.RespondJSON(c, new {
                    Error = "Unauthorized - invalid key."
                });
                return;
            }

            f.Server.UserData.Delete<LastChannelUserInfo>(uid);
            f.Server.UserData.Delete<ChatModule.UserChatSettings>(uid);
            f.Server.UserData.Delete<Chat.Cmd.TPSettings>(uid);

            f.RespondJSON(c, new {
                Info = "Data reset."
            });
        }

        [RCEndpoint(false, "/avatar", "?uid={uid}&fallback={true|false}", "", "Get Avatar", "Get a 64x64 round user avatar PNG.")]
        public static void Avatar(Frontend f, HttpRequestEventArgs c) {
            NameValueCollection args = f.ParseQueryString(c.Request.RawUrl);

            string? uid = args["uid"];
            if (uid.IsNullOrEmpty()) {
                if (bool.TryParse(args["fallback"], out bool fallback) && fallback) {
                    f.RespondContent(c, "frontend/assets/avatar_fallback.png");
                    return;
                }

                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                f.RespondJSON(c, new {
                    Error = "No UID."
                });
                return;
            }

            Stream? data = f.Server.UserData.ReadFile(uid, "avatar.png");
            if (data == null) {
                if (bool.TryParse(args["fallback"], out bool fallback) && fallback) {
                    f.RespondContent(c, "frontend/assets/avatar_fallback.png");
                    return;
                }

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
