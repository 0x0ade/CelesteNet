namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDTagAdd : WSCMD {
        public override bool MustAuth => true;
        public override bool MustAuthExec => true;
        public override object? Run(dynamic? data) {
            string? uid = data?.UID, tag = data?.Tag;
            if (uid.IsNullOrEmpty() || tag.IsNullOrEmpty())
                return null;
            if (!Frontend.Server.UserData.TryLoad(uid, out BasicUserInfo info))
                return null;
            info.Tags.Add(tag);
            Frontend.Server.UserData.Save(uid, info);
            if (tag == BasicUserInfo.TAG_AUTH || tag == BasicUserInfo.TAG_AUTH_EXEC)
                Frontend.Server.UserData.Create(uid, true);
            return null;
        }
    }

    public class WSCMDTagRemove : WSCMD {
        public override bool MustAuth => true;
        public override bool MustAuthExec => true;
        public override object? Run(dynamic? data) {
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
