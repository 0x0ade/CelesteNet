using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Utils
{
    internal class TokenUtils
    {
        public static bool refreshToken()
        {
            var result = HttpUtils.Post("https://celeste.centralteam.cn/oauth/token", "\r\n{\"client_id\":\"ccE8Ulzu4ObVUlWmSozW7CUtc6zmfAQd\",\r\n\"client_secret\":\"Fzojlor7EuxB6KT2juQoTTuAs9Is2F\",\r\n\"grant_type\":\"refresh_token\",\r\n\"refresh_token\":\"" + CelesteNetClientModule.Settings.RefreshToken + "\",\r\n\"redirect_uri\":\"http://localhost:38038/auth\"\r\n}\r\n");
            dynamic json = JsonConvert.DeserializeObject(result);
            if(json?.error != null)
            {
                return false;
            }
            CelesteNetClientModule.Settings.Key = json.access_token;
            CelesteNetClientModule.Settings.RefreshToken = json.refresh_token;
            System.DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new System.DateTime(1970, 1, 1)); //
            long timeStamp = (long)(DateTime.Now - startTime).TotalSeconds;
            CelesteNetClientModule.Settings.ExpiredTime = (timeStamp + (int)json.expires_in).ToString();
            CelesteNetClientModule.Instance.SaveSettings();
            return true;
        }
    }
}
