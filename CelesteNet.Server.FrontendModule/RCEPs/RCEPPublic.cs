using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net.Http;
using WebSocketSharp.Net;
using WebSocketSharp.Server;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public static partial class RCEndpoints {

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
                    avatarOrig = Image.Load(s);
                } catch {
                    using Stream s = client.GetAsync(
                        $"https://cdn.discordapp.com/embed/avatars/{((int) userData.discriminator) % 6}.png"
                    ).Await().Content.ReadAsStreamAsync().Await();
                    avatarOrig = Image.Load(s);
                }
            }

            using (avatarOrig)
            using (Image avatarScale = avatarOrig.Clone(x => x.Resize(64, 64, sampler: KnownResamplers.Lanczos3)))
            using (Image avatarFinal = avatarOrig.Clone(x => x.Resize(64, 64, sampler: KnownResamplers.Lanczos3).ApplyRoundedCorners(1.0f))) {

                using (Stream s = f.Server.UserData.WriteFile(uid, "avatar.orig.png"))
                    avatarScale.SaveAsPng(s);

                using (Stream s = f.Server.UserData.WriteFile(uid, "avatar.png"))
                    avatarFinal.SaveAsPng(s);
            }

            c.Response.StatusCode = (int) HttpStatusCode.Redirect;
            c.Response.Headers.Set("Location", $"http://{c.Request.UserHostName}/");
            f.SetKeyCookie(c, key);
            f.SetDiscordAuthCookie(c, code);
            f.RespondJSON(c, new {
                Info = "Success - redirecting to /"
            });
        }

        // This method can be seen as an inline implementation of an `IImageProcessor`:
        // (The combination of `IImageOperations.Apply()` + this could be replaced with an `IImageProcessor`)
        private static IImageProcessingContext ApplyRoundedCorners(this IImageProcessingContext context, float cornerRadius) {
            Size size = context.GetCurrentSize();
            IPathCollection corners = BuildCorners(size.Width, size.Height, cornerRadius);

            context.SetGraphicsOptions(new GraphicsOptions() {
                Antialias = true,

                // Enforces that any part of this shape that has color is punched out of the background
                AlphaCompositionMode = PixelAlphaCompositionMode.DestOut
            });

            // Mutating in here as we already have a cloned original
            // use any color (not Transparent), so the corners will be clipped
            foreach (IPath path in corners) {
                context = context.Fill(Color.Red, path);
            }

            return context;
        }

        private static IPathCollection BuildCorners(int imageWidth, int imageHeight, float cornerRadius) {
            // First create a square
            var rect = new RectangularPolygon(-0.5f, -0.5f, cornerRadius, cornerRadius);

            // Then cut out of the square a circle so we are left with a corner
            IPath cornerTopLeft = rect.Clip(new EllipsePolygon(cornerRadius - 0.5f, cornerRadius - 0.5f, cornerRadius));

            // Corner is now a corner shape positions top left
            // let's make 3 more positioned correctly, we can do that by translating the original around the center of the image.

            float rightPos = imageWidth - cornerTopLeft.Bounds.Width + 1;
            float bottomPos = imageHeight - cornerTopLeft.Bounds.Height + 1;

            // Move it across the width of the image - the width of the shape
            IPath cornerTopRight = cornerTopLeft.RotateDegree(90).Translate(rightPos, 0);
            IPath cornerBottomLeft = cornerTopLeft.RotateDegree(-90).Translate(0, bottomPos);
            IPath cornerBottomRight = cornerTopLeft.RotateDegree(180).Translate(rightPos, bottomPos);

            return new PathCollection(cornerTopLeft, cornerBottomLeft, cornerTopRight, cornerBottomRight);
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
