using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Collections.Specialized;
using Celeste.Mod.CelesteNet.Client.Utils;
using Newtonsoft.Json;

namespace Celeste.Mod.CelesteNet.Client
{
    // Based off of Everest's own DebugRC.
    public static class CelesteNetClientRC
    {

        private static readonly char[] URLArgsSeperator = new[] { '&' };

        private static HttpListener Listener;
        private static Thread ListenerThread;

        public static void Initialize()
        {
            if (Listener != null)
                return;

            try
            {
                Listener = new();
                // Port MUST be fixed as the website expects it to be the same for everyone.
                Listener.Prefixes.Add($"http://localhost:{CelesteNetUtils.ClientRCPort}/");
                Listener.Start();
            }
            catch (Exception e)
            {
                e.LogDetailed();
                try
                {
                    Listener?.Stop();
                }
                catch { }
                return;
            }

            ListenerThread = new(ListenerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            ListenerThread.Start();
        }

        public static void Shutdown()
        {
            Listener?.Abort();
            ListenerThread?.Abort();
            Listener = null;
            ListenerThread = null;
        }

        private static void ListenerLoop()
        {
            Logger.Log(LogLevel.INF, "rc", $"Started ClientRC thread, available via http://localhost:{CelesteNetUtils.ClientRCPort}/");
            try
            {
                while (Listener?.IsListening ?? false)
                {
                    ThreadPool.QueueUserWorkItem(c =>
                    {
                        HttpListenerContext context = c as HttpListenerContext;

                        if (context.Request.HttpMethod == "OPTIONS")
                        {
                            context.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, Accept, X-Requested-With");
                            context.Response.AddHeader("Access-Control-Allow-Methods", "GET, POST");
                            context.Response.AddHeader("Access-Control-Max-Age", "1728000");
                            return;
                        }
                        context.Response.AppendHeader("Access-Control-Allow-Origin", "*");

                        try
                        {
                            using (context.Request.InputStream)
                            using (context.Response)
                            {
                                HandleRequest(context);
                            }
                        }
                        catch (ThreadAbortException)
                        {
                            throw;
                        }
                        catch (ThreadInterruptedException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            Logger.Log(LogLevel.INF, "rc", $"ClientRC failed responding: {e}");
                        }
                    }, Listener.GetContext());
                }
            }
            catch (ThreadAbortException)
            {
                throw;
            }
            catch (ThreadInterruptedException)
            {
                throw;
            }
            catch (HttpListenerException e)
            {
                // 500 = Listener closed.
                // 995 = I/O abort due to thread abort or application shutdown.
                if (e.ErrorCode != 500 &&
                    e.ErrorCode != 995)
                {
                    Logger.Log(LogLevel.INF, "rc", $"ClientRC failed listening ({e.ErrorCode}): {e}");
                }
            }
            catch (Exception e)
            {
                Logger.Log(LogLevel.INF, "rc", $"ClientRC failed listening: {e}");
            }
        }

        private static void HandleRequest(HttpListenerContext c)
        {
            Logger.Log(LogLevel.VVV, "rc", $"Requested: {c.Request.RawUrl}");

            string url = c.Request.RawUrl;
            int indexOfSplit = url.IndexOf('?');
            if (indexOfSplit != -1)
                url = url.Substring(0, indexOfSplit);

            RCEndPoint endpoint =
                EndPoints.FirstOrDefault(ep => ep.Path == c.Request.RawUrl) ??
                EndPoints.FirstOrDefault(ep => ep.Path == url) ??
                EndPoints.FirstOrDefault(ep => ep.Path.ToLowerInvariant() == c.Request.RawUrl.ToLowerInvariant()) ??
                EndPoints.FirstOrDefault(ep => ep.Path.ToLowerInvariant() == url.ToLowerInvariant()) ??
                EndPoints.FirstOrDefault(ep => ep.Path == "/404");
            endpoint.Handle(c);
        }


        #region Read / Parse Helpers

        public static NameValueCollection ParseQueryString(string url)
        {
            NameValueCollection nvc = new();

            int indexOfSplit = url.IndexOf('?');
            if (indexOfSplit == -1)
                return nvc;
            url = url.Substring(indexOfSplit + 1);

            string[] args = url.Split(URLArgsSeperator);
            foreach (string arg in args)
            {
                indexOfSplit = arg.IndexOf('=');
                if (indexOfSplit == -1)
                    continue;
                nvc[arg.Substring(0, indexOfSplit)] = arg.Substring(indexOfSplit + 1);
            }

            return nvc;
        }

        #endregion

        #region Write Helpers

        public static void WriteHTMLStart(HttpListenerContext c, StringBuilder builder)
        {
            builder.Append(
@"<!DOCTYPE html>
<html>
    <head>
        <meta charset=""utf-8"" />
        <meta name=""viewport"" content=""width=device-width, initial-scale=1, user-scalable=no"" />
        <title>CelesteNet ClientRC</title>
        <style>
@font-face {
    font-family: Renogare;
    src:
    url(""https://everestapi.github.io/fonts/Renogare-Regular.woff"") format(""woff""),
    url(""https://everestapi.github.io/fonts/Renogare-Regular.otf"") format(""opentype"");
}
body {
    color: rgba(0, 0, 0, 0.87);
    font-family: sans-serif;
    padding: 0;
    margin: 0;
    line-height: 1.5em;
}
header {
    background: #3b2d4a;
    color: white;
    font-family: Renogare, sans-serif;
    font-size: 32px;
    position: sticky;
    top: 0;
    left: 0;
    right: 0;
    height: 64px;
    line-height: 64px;
    padding: 8px 48px;
    z-index: 100;
}
#main {
    position: relative;
    margin: 8px;
    min-height: 100vh;
}
#endpoints li h3 {
    margin-bottom: 0;
}
#endpoints li p {
    margin-top: 0;
}
        </style>
    </head>
    <body>
"
            );

            builder.AppendLine(@"<header>CelesteNet ClientRC</header>");
            builder.AppendLine(@"<div id=""main"">");
        }

        public static void WriteHTMLEnd(HttpListenerContext c, StringBuilder builder)
        {
            builder.AppendLine(@"</div>");

            builder.Append(
@"
    </body>
</html>
"
            );
        }

        public static void Write(HttpListenerContext c, string str)
        {
            byte[] buf = CelesteNetUtils.UTF8NoBOM.GetBytes(str);
            c.Response.ContentLength64 = buf.Length;
            c.Response.OutputStream.Write(buf, 0, buf.Length);
        }

        #endregion

        #region Default RCEndPoint Handlers

        public static List<RCEndPoint> EndPoints = new() {

                new RCEndPoint {
                    Path = "/",
                    Name = "Info",
                    InfoHTML = "Basic CelesteNet ClientRC info.",
                    Handle = c => {
                        StringBuilder builder = new();

                        WriteHTMLStart(c, builder);

                        builder.AppendLine(
@"<ul>
<h2>Info</h2>
<li>
    This weird website exists so that the main CelesteNet website can give your clients special commands.<br>
    For example, clicking on the ""send key to client"" button will send it to /setname.<br>
    It's only accessible from your local machine, meaning that nobody else can see this.
</li>
</ul>"
                        );

                        builder.AppendLine(@"<ul id=""endpoints"">");
                        builder.AppendLine(@"<h2>Endpoints</h2>");
                        foreach (RCEndPoint ep in EndPoints) {
                            builder.AppendLine(@"<li>");
                            builder.AppendLine($@"<h3>{ep.Name}</h3>");
                            builder.AppendLine($@"<p><a href=""{ep.PathExample ?? ep.Path}""><code>{ep.PathHelp ?? ep.Path}</code></a><br>{ep.InfoHTML}</p>");
                            builder.AppendLine(@"</li>");
                        }
                        builder.AppendLine(@"</ul>");

                        WriteHTMLEnd(c, builder);

                        Write(c, builder.ToString());
                    }
                },

                new RCEndPoint {
                    Path = "/404",
                    Name = "404",
                    InfoHTML = "Basic 404.",
                    Handle = c => {
                        c.Response.StatusCode = (int) HttpStatusCode.NotFound;
                        Write(c, "ERROR: Endpoint not found.");
                    }
                },

                new RCEndPoint {
                    Path = "/auth",
                    PathHelp = "/auth?code={key} (Example: ?code=1a2b3d4e)",
                    PathExample = "/auth?code=Guest",
                    Name = "AuthCode",
                    InfoHTML = "Set Auth Code to Get Token",
                    Handle = c => {
                        NameValueCollection data = ParseQueryString(c.Request.RawUrl.Replace("#","?"));
                        Console.WriteLine(data);
                        if (data.AllKeys.Contains("code"))
                        {
                            string name = data["code"].Trim();
                            if (string.IsNullOrEmpty(name)) {
                                c.Response.StatusCode = (int) HttpStatusCode.BadRequest;
                                Write(c, $"ERROR: No value given.");
                                return;
                            }
                            try
                            {
                                string clientId = "DEV";
                                string clientSecret ="DEV";
                                var result =  HttpUtils.Post("https://celeste.centralteam.cn/oauth/token","\r\n{\"client_id\":\""+clientId+"\",\r\n\"client_secret\":\""+clientSecret+"\",\r\n\"grant_type\":\"authorization_code\",\r\n\"code\":\""+name+"\",\r\n\"redirect_uri\":\"http://localhost:38038/auth\"\r\n}\r\n");
                                dynamic json = JsonConvert.DeserializeObject(result);
                                CelesteNetClientModule.Settings.Key = json.access_token;
                                CelesteNetClientModule.Settings.RefreshToken = json.refresh_token;
                                System.DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1)); //
                                long timeStamp = (long)(DateTime.Now - startTime).TotalSeconds;
                                CelesteNetClientModule.Settings.ExpiredTime = (timeStamp + (int)json.expires_in).ToString();
                                CelesteNetClientModule.Instance.SaveSettings();
                                CelesteNetClientModule.Settings.Connected = true;
                                Write(c, "Login Success,Please Closed this Page.");
                            }catch(Exception e){
                                Write(c, "Login Failed,Please Retry. Log:\n"+e.Message);
                            }
                        }

                    }
                }
            };

        #endregion

    }
}
