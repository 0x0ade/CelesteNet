using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Cecil;
using Mono.Options;
using System;
using System.Collections.Generic;
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

        public CelesteNetServerModule Module;

        public Assembly Assembly;

        public HashSet<string> References;
        public HashSet<CelesteNetServerModuleWrapper> ReferredBy = new HashSet<CelesteNetServerModuleWrapper>();

        public CelesteNetServerModuleWrapper(CelesteNetServer server, string path) {
            Server = server;
            AssemblyPath = path;

            using (ModuleDefinition module = ModuleDefinition.ReadModule(AssemblyPath)) {
                ID = module.Assembly.Name.Name;
                References = new HashSet<string>(
                    module.AssemblyReferences
                    .Select(name => name.Name)
                    .Where(name => name.StartsWith("CelesteNet.Server.") && name.EndsWith("Module"))
                );
            }

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

            foreach (Type type in Assembly.GetTypes()) {
                if (typeof(CelesteNetServerModule).IsAssignableFrom(type) && !type.IsAbstract) {
                    Module = (CelesteNetServerModule) Activator.CreateInstance(type);
                    break;
                }
            }

            if (Module == null)
                throw new Exception($"Found no module class in {ID} - {AssemblyPath}");

            lock (Server.Modules) {
                Server.Modules.Add(Module);
                Server.ModuleMap[Module.GetType()] = Module;
            }
        }

        public void Unload() {
            if (Module == null)
                return;

            foreach (CelesteNetServerModuleWrapper other in ReferredBy)
                other.Unload();

            Logger.Log(LogLevel.INF, "module", $"Unloading {ID}");

            lock (Server.Modules) {
                Server.Modules.Remove(Module);
                Server.ModuleMap.Clear();
            }
            
            Module.Dispose();
            Module = null;

            Server.DetourModManager.Unload(Assembly);

            UnloadAssembly();
            Assembly = null;
        }

    }
}
