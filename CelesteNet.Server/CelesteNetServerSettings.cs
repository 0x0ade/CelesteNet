using Microsoft.Xna.Framework;
using Mono.Options;
using Monocle;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetServerSettings : CelesteNetServerModuleSettings {

        [YamlIgnore]
        [JsonIgnore]
        public static string DefaultFilePath = "celestenet-config.yaml";

        public override void Load(string path = "") {
            base.Load(path.Nullify() ?? FilePath.Nullify() ?? DefaultFilePath);
        }

        public override void Save(string path = "") {
            base.Save(path.Nullify() ?? FilePath.Nullify() ?? DefaultFilePath);
        }

        public string ModuleRoot { get; set; } = "Modules";
        public string ModuleConfigRoot { get; set; } = "ModuleConfigs";
        public string UserDataRoot { get; set; } = "UserData";

        public int HeuristicSampleWindow { get; set; } = 800;

        public int NetPlusThreadPoolThreads { get; set; } = -1;
        public int NetPlusMaxThreadRestarts { get; set; } = 5;
        public float NetPlusSchedulerInterval { get; set; } = 10000;
        public float NetPlusSchedulerUnderloadThreshold { get; set; } = 0.1f;
        public float NetPlusSchedulerOverloadThreshold { get; set; } = 0.9f;
        public float NetPlusSchedulerStealThreshold { get; set; } = 0.7f;

        public int MainPort { get; set; } = 3802;
        
        public int MaxPacketSize { get; set; } = 1024;
        public int MaxQueueSize { get; set; } = 256;
        public int MaxTickRate { get; set; } = 60;
        public float MergeWindow { get; set; } = 40;
        public float HeartbeatInterval { get; set; } = 1000f;

        public bool TCPRecvUseEPoll { get; set; } = true;
        public int TCPPollMaxEvents { get; set; } = 16;
        public int TCPBufferSize { get; set; } = 16384;
        public int TCPSockSendBufferSize { get; set; } = 65536;
        public int UDPMaxDatagramSize { get; set; } = 4096;

        public float PlayerTCPDownlinkBpTCap { get; set; } = 256;
        public float PlayerTCPDownlinkPpTCap { get; set; } = 8;
        public float PlayerTCPUplinkBpTCap { get; set; } = 4096;
        public float PlayerTCPUplinkPpTCap { get; set; } = 64;
        public float PlayerUDPDownlinkBpTCap { get; set; } = 512;
        public float PlayerUDPDownlinkPpTCap { get; set; } = 16;
        public float PlayerUDPUplinkBpTCap { get; set; } = 8192;
        public float PlayerUDPUplinkPpTCap { get; set; } = 128;

        public LogLevel LogLevel {
            get => Logger.Level;
            set => Logger.Level = value;
        }

        public int MaxNameLength { get; set; } = 30;
        public int MaxEmoteValueLength { get; set; } = 2048;
        public int MaxChannelNameLength { get; set; } = 16;

        public byte MaxHairLength { get; set; } = 12;
        public byte MaxFollowers { get; set; } = 12;

        public bool AuthOnly { get; set; } = false;

        public string MessageTeapotVersionMismatch { get; set; } = "Teapot version mismatch";
        public string MessageAuthOnly { get; set; } = "Server supports only authenticated clients";
        public string MessageInvalidKey { get; set; } = "Invalid key";
        public string MessageBan { get; set; } = "Banned: {4}";

    }
}
