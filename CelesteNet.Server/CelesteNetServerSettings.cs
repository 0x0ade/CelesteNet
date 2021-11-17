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

        public int NetPlusThreadPoolThreads { get; set; } = -1;
        public int NetPlusMaxThreadRestarts { get; set; } = 5;
        public int NetPlusHeuristicSampleWindow { get; set; } = 800;
        public float NetPlusSchedulerInterval { get; set; } = 10000;
        public float NetPlusSchedulerUnderloadThreshold { get; set; } = 0.1f;
        public float NetPlusSchedulerOverloadThreshold { get; set; } = 0.9f;
        public float NetPlusSchedulerStealThreshold { get; set; } = 0.7f;

        public int MainPort { get; set; } = 3802;

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
