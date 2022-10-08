using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDUnban : WSCMD<string> {
        public override bool MustAuth => true;
        public override object? Run(string uid) {
            if ((uid = uid?.Trim() ?? "").IsNullOrEmpty())
                return null;

            Frontend.Server.UserData.Delete<BanInfo>(uid);
            Frontend.BroadcastCMD(true, "update", Frontend.Settings.APIPrefix + "/userinfos");

            return null;
        }
    }
}
