#if NETCORE
using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public partial class CelesteNetServerModuleWrapper {

        private AssemblyLoadContext ALC;

        private void LoadAssembly() {
            ALC = new AssemblyLoadContext(null, true);
            Assembly = ALC.LoadFromAssemblyPath(AssemblyPath);
        }

        private void UnloadAssembly() {
            ALC.Unload();
            ALC = null;
        }

    }
}
#endif
