using System;

namespace Celeste.Mod.CelesteNet {
    public class CelesteNetClientOptions {
        public bool IsReconnect;
        public bool AvatarsDisabled = false;
        public ulong ClientID;
        public uint InstanceID;
        // Due to how these are sent as packets, this needs to be a ulong.
        public ulong SupportedClientFeatures = 0;
    }

    [Flags]
    public enum CelesteNetSupportedClientFeatures : ulong {
        LocateCommand = 1 >> 0
    }
}