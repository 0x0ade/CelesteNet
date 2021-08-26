using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Cecil;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public partial class CelesteNetServerModuleWrapper {

        public readonly CelesteNetServer Server;
        public readonly string AssemblyPath;
        public readonly string ID;

        public CelesteNetServerModule? Module;
        public Assembly? Assembly;

        public HashSet<string> References;
        public HashSet<CelesteNetServerModuleWrapper> ReferredBy = new();

        public CelesteNetServerModuleWrapper(CelesteNetServer server, string path) {
            Server = server;
            AssemblyPath = path;

            using ModuleDefinition module = ModuleDefinition.ReadModule(AssemblyPath);
            ID = module.Assembly.Name.Name;
            References = new(
                module.AssemblyReferences
                .Select(name => name.Name)
                .Where(name => name.StartsWith("CelesteNet.Server.") && name.EndsWith("Module"))
            );

            Logger.Log(LogLevel.INF, "module", $"New module {ID} - {path}");
        }

        public void Reload() {
            Logger.Log(LogLevel.INF, "module", $"Reloading {ID}");

            Unload();
            Load();

            foreach (CelesteNetServerModuleWrapper other in ReferredBy)
                other.Reload();
        }

        public void Load() {
            if (Module != null)
                return;
            Logger.Log(LogLevel.INF, "module", $"Loading {ID}");

            LoadAssembly();

            if (Assembly == null)
                throw new Exception($"Failed to load assembly for {ID} - {AssemblyPath}");

            foreach (Type type in Assembly.GetTypes()) {
                if (typeof(CelesteNetServerModule).IsAssignableFrom(type) && !type.IsAbstract) {
                    Module = (CelesteNetServerModule?) Activator.CreateInstance(type);
                    break;
                }
            }

            if (Module == null)
                throw new Exception($"Found no module class in {ID} - {AssemblyPath}");

            lock (Server.Modules) {
                Server.Modules.Add(Module);
                Server.ModuleMap[Module.GetType()] = Module;
            }

            if (Server.Initialized) {
                Logger.Log(LogLevel.INF, "module", $"Initializing {ID} (late)");
                Module.Init(this);
                if (Server.IsAlive) {
                    Logger.Log(LogLevel.INF, "module", $"Starting {ID} (late)");
                    Module.Start();
                }
                Server.Data.RescanDataTypes(Assembly.GetTypes());
            }
        }

        public void Unload() {
            if (Module == null || Assembly == null)
                return;

            foreach (CelesteNetServerModuleWrapper other in ReferredBy)
                other.Unload();

            Logger.Log(LogLevel.INF, "module", $"Unloading {ID}");

            Module.Dispose();

            lock (Server.Modules) {
                Server.Modules.Remove(Module);
                Server.ModuleMap.Clear();
            }

            Module = null;

            Server.DetourModManager.Unload(Assembly);
            Server.Data.RemoveDataTypes(Assembly.GetTypes());

            UnloadAssembly();
            Assembly = null;
        }

    }
}
