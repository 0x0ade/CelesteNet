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
    public class WSCMDAddTag : WSCMD {
        public override bool Auth => true;
        public override object? Run(dynamic? data) {
            if (!Frontend.CurrentSessionExecKeys.Contains(WS.SessionKey))
                return null;
            string? uid = data?.UID, tag = data?.Tag;
            if (uid.IsNullOrEmpty() || tag.IsNullOrEmpty())
                return null;
            if (!Frontend.Server.UserData.TryLoad(uid, out BasicUserInfo info))
                return null;
            info.Tags.Add(tag);
            Frontend.Server.UserData.Save(uid, info);
            return null;
        }
    }

    public class WSCMDRemoveTag : WSCMD {
        public override bool Auth => true;
        public override object? Run(dynamic? data) {
            if (!Frontend.CurrentSessionExecKeys.Contains(WS.SessionKey))
                return null;
            string? uid = data?.UID, tag = data?.Tag;
            if (uid.IsNullOrEmpty() || tag.IsNullOrEmpty())
                return null;
            if (!Frontend.Server.UserData.TryLoad(uid, out BasicUserInfo info))
                return null;
            info.Tags.Remove(tag);
            Frontend.Server.UserData.Save(uid, info);
            return null;
        }
    }
}
