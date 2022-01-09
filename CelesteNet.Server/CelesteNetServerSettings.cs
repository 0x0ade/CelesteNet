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
        public float NetPlusSchedulerOverloadThreshold { get; set; } = 0.8f;
        public float NetPlusSchedulerStealThreshold { get; set; } = 0.7f;

        public int MainPort { get; set; } = 17230;

        public int MaxTickRate { get; set; } = 60;
        public float TickRateLowActivityThreshold { get; set; } = 0.3f;
        public float TickRateLowTCPUplinkBpSThreshold { get; set; } = 8388608; // 8 MBpS
        public float TickRateLowUDPUplinkBpSThreshold { get; set; } = 8388608; // 8 MBpS
        public float TickRateHighActivityThreshold { get; set; } = 0.85f;
        public float TickRateHighTCPUplinkBpSThreshold { get; set; } = 33554432; // 32 MBpS
        public float TickRateHighUDPUplinkBpSThreshold { get; set; } = 33554432; // 32 MBpS

        public int MaxPacketSize { get; set; } = 2048;
        public int MaxQueueSize { get; set; } = 1024;
        public float MergeWindow { get; set; } = 15;
        public int MaxHeartbeatDelay { get; set; } = 20;
        public float HeartbeatInterval { get; set; } = 250f;

        public string PacketDumperDirectory { get; set; } = "packetDump";
        public int PacketDumperMaxDumps { get; set; } = 64;

        public bool TCPRecvUseEPoll { get; set; } = true;
        public int TCPPollMaxEvents { get; set; } = 16;
        public int TCPRecvBufferSize { get; set; } = 16384;
        public int TCPSendMaxRetries { get; set; } = 8;
        public int UDPMaxDatagramSize { get; set; } = 16384;

        public int UDPAliveScoreMax { get; set; } = 70;
        public int UDPDowngradeScoreMin { get; set; } = -2;
        public int UDPDowngradeScoreMax { get; set; } = 2;
        public int UDPDeathScoreMin { get; set; } = -2;
        public int UDPDeathScoreMax { get; set; } = 2;

        public float PlayerTCPDownlinkBpTCap { get; set; } = 1024;
        public float PlayerTCPDownlinkPpTCap { get; set; } = 64;
        public float PlayerTCPUplinkBpTCap { get; set; } = 4096;
        public float PlayerTCPUplinkPpTCap { get; set; } = 128;
        public float PlayerUDPDownlinkBpTCap { get; set; } = 1024;
        public float PlayerUDPDownlinkPpTCap { get; set; } = 64;
        public float PlayerUDPUplinkBpTCap { get; set; } = 4096;
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

        public string MessageDiscontinue { get; set; } = "";
        public string MessageTeapotVersionMismatch { get; set; } = "Teapot version mismatch";
        public string MessageAuthOnly { get; set; } = "Server supports only authenticated clients";
        public string MessageInvalidKey { get; set; } = "Invalid key";
        public string MessageBan { get; set; } = "Banned: {2}";

    }
}
