using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

        // See https://github.com/dotnet/runtime/blob/144e5145453ac3885ac20bc1f1f2641523c6fcea/src/libraries/System.Private.CoreLib/src/System/String.cs#L488
        public static bool IsNullOrEmpty([NotNullWhen(false)] this string? value)
            => string.IsNullOrEmpty(value);

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
            => (Everest.Modules?.Select(m => m.GetType().Assembly) ?? new Assembly[0])
            .Concat(AppDomain.CurrentDomain.GetAssemblies())
            .Distinct();

        private static Type[] _GetTypes()
            => _GetAssemblies().SelectMany(_GetTypes).ToArray();

        private static IEnumerable<Type> _GetTypes(Assembly asm) {
            try {
                return asm.GetTypes();
            } catch (ReflectionTypeLoadException e) {
                return e.Types.Where(t => t != null);
            }
        }

        public static string ToHex(this Color c)
            => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Type? GetTypeBoundTo(this IDataBoundRef data)
            => data.GetType()
            .GetInterfaces()
            .FirstOrDefault(t => t.IsConstructedGenericType && t.GetGenericTypeDefinition() == typeof(IDataBoundRef<>))
            ?.GetGenericArguments()[0];

    }
}
