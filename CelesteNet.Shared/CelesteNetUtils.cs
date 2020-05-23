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

        public static Type[] GetTypes() {
            Type[] typesPrev = _GetTypes();
            Retry:
            Type[] types = _GetTypes();
            if (typesPrev.Length != types.Length) {
                typesPrev = types;
                goto Retry;
            }
            return types;
        }

        private static Type[] _GetTypes()
            => AppDomain.CurrentDomain.GetAssemblies().SelectMany(asm => {
                try {
                    return asm.GetTypes();
                } catch (ReflectionTypeLoadException e) {
                    return e.Types.Where(t => t != null);
                }
            }).ToArray();

        public static string ToHex(this Color c)
            => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    }
}
