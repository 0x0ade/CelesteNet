#if NETFRAMEWORK
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

        private static readonly Dictionary<string, string> AssemblyNameMap = new Dictionary<string, string>();

        private AssemblyName? AssemblyNameReal;
        private AssemblyName? AssemblyNameNew;

        private void LoadAssembly() {
            long stamp = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;

            string dir = Path.Combine(Path.GetTempPath(), "CelesteNetServerModuleCache");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(AssemblyPath)}.{stamp}.dll");

            using (ModuleDefinition module = ModuleDefinition.ReadModule(AssemblyPath)) {
                AssemblyNameReal = new AssemblyName(module.Assembly.Name.FullName);

                module.Name += "." + stamp;
                module.Assembly.Name.Name += "." + stamp;

                foreach (AssemblyNameReference reference in module.AssemblyReferences)
                    if (AssemblyNameMap.TryGetValue(reference.Name, out string referenceNew))
                        reference.Name = referenceNew;

                module.Write(path);

                AssemblyNameNew = new AssemblyName(module.Assembly.Name.FullName);
                AssemblyNameMap[AssemblyNameReal.Name] = AssemblyNameNew.Name;
            }

            Assembly = Assembly.LoadFrom(path);

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private Assembly? OnAssemblyResolve(object sender, ResolveEventArgs args) {
            if (AssemblyNameReal == null ||
                AssemblyNameNew == null)
                return null;

            AssemblyName name = new AssemblyName(args.Name);
            if (name.FullName == AssemblyNameReal.FullName ||
                name.FullName == AssemblyNameNew.FullName ||
                name.Name == AssemblyNameReal.Name ||
                name.Name == AssemblyNameNew.Name)
                return Assembly;

            return null;
        }

        private void UnloadAssembly() {
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
        }

    }
}
#endif
