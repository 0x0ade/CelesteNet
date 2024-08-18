using System;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetClientOptions {
        public bool IsReconnect;
        public bool AvatarsDisabled = false;
        public ulong ClientID;
        public uint InstanceID;
        public CelesteNetSupportedClientFeatures SupportedClientFeatures = CelesteNetSupportedClientFeatures.None;
    }

    [Flags]
    public enum CelesteNetSupportedClientFeatures : ulong {
        None = 0,
        LocateCommand = 1 << 0
    }
}