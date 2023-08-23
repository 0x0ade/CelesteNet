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
    public class WSCMDTagAdd : WSCMD {
        public override bool MustAuth => true;
        public override bool MustAuthExec => true;
        public override object? Run(dynamic? data) {
            string? uid = data?.UID, tag = data?.Tag;
            if (uid.IsNullOrEmpty() || tag.IsNullOrEmpty())
                return false;
            if (!Frontend.Server.UserData.TryLoad(uid, out BasicUserInfo info))
                return false;
            info.Tags.Add(tag);
            Frontend.Server.UserData.Save(uid, info);
            if (tag == BasicUserInfo.TAG_AUTH || tag == BasicUserInfo.TAG_AUTH_EXEC)
                Frontend.Server.UserData.Create(uid, true);
            return true;
        }
    }

    public class WSCMDTagRemove : WSCMD {
        public override bool MustAuth => true;
        public override bool MustAuthExec => true;
        public override object? Run(dynamic? data) {
            string? uid = data?.UID, tag = data?.Tag;
            if (uid.IsNullOrEmpty() || tag.IsNullOrEmpty())
                return false;
            if (!Frontend.Server.UserData.TryLoad(uid, out BasicUserInfo info))
                return false;
            info.Tags.Remove(tag);
            Frontend.Server.UserData.Save(uid, info);
            return true;
        }
    }
}
