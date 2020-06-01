using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public static class CelesteNetUtils {

        public const ushort Version = 1;
        public static readonly ushort LoadedVersion = Version;

        public static readonly string HTTPTeapot = "HTTP/1.1 418 I'm a teapot\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";

        public static Type[] GetTypes() {
            if (Everest.Modules.Count != 0)
                return _GetTypes();

            Type[] typesPrev = _GetTypes();
            Retry:
            Type[] types = _GetTypes();
            if (typesPrev.Length != types.Length) {
                typesPrev = types;
                goto Retry;
            }
            return types;
        }

        private static IEnumerable<Assembly> _GetAssemblies()
            => AppDomain.CurrentDomain.GetAssemblies()
            .Concat(Everest.Modules.Select(m => m.GetType().Assembly))
            .Distinct();

        private static Type[] _GetTypes()
            => AppDomain.CurrentDomain.GetAssemblies().SelectMany(_GetTypes).ToArray();

        private static IEnumerable<Type> _GetTypes(Assembly asm) {
            try {
                return asm.GetTypes();
            } catch (ReflectionTypeLoadException e) {
                return e.Types.Where(t => t != null);
            }
        }

        public static string ToHex(this Color c)
            => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    }
}
