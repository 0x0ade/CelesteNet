using System;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetClientOptions {
        public bool IsReconnect;
        public bool AvatarsDisabled = false;
        public ulong ClientID;
        public uint InstanceID;
        public CelesteNetSupportedClientFeatures SupportedClientFeatures;
    }

    [Flags]
    public enum CelesteNetSupportedClientFeatures : ulong {}
}