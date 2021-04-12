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
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public static partial class CelesteNetUtils {

        public const ushort Version = 2;
        public static readonly ushort LoadedVersion = Version;

        // Port MUST be fixed as the website expects it to be the same for everyone.
        public static readonly int ClientRCPort = 38038;

        public static readonly Encoding UTF8NoBOM = new UTF8Encoding(false);

        public static readonly string HTTPTeapotConFeatures = "CelesteNet-ConnectionFeatures: ";
        public static readonly string HTTPTeapotConToken = "CelesteNet-ConnectionToken: ";
        public static readonly string HTTPTeapot = $"HTTP/1.1 418 I'm a teapot\r\nContent-Length: 0\r\n{HTTPTeapotConFeatures}{{0}}\r\n{HTTPTeapotConToken}{{1}}\r\nConnection: close\r\n\r\n";

        public static readonly char[] ConnectionFeatureSeparators = { ';' };
        public static readonly string ConnectionFeatureSeparator = ";";

        public static readonly string[] ConnectionFeaturesBuiltIn = { StringMap.ConnectionFeature };

        // See https://github.com/dotnet/runtime/blob/144e5145453ac3885ac20bc1f1f2641523c6fcea/src/libraries/System.Private.CoreLib/src/System/String.cs#L488
        public static bool IsNullOrEmpty([NotNullWhen(false)] this string? value)
            => string.IsNullOrEmpty(value);

        public static string? Nullify(this string? value)
            => string.IsNullOrEmpty(value) ? null : value;

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
#pragma warning disable CS8619 // Compiler thinks this could be <Type?> even though we check for t != null
                return e.Types.Where(t => t != null);
#pragma warning restore CS8619
            }
        }

        public static string ToHex(this Color c)
            => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        public static Type GetRequestType(this Type t) {
            Type[] interfaces = t.GetInterfaces();
            foreach (Type i in interfaces)
                if (i.IsConstructedGenericType && i.GetGenericTypeDefinition() == typeof(IDataRequestable<>))
                    return i.GetGenericArguments()[0];
            throw new Exception($"Invalid requested type: {t.FullName}");
        }

        public static readonly Regex SanitizeRegex = new Regex(@"[^\w\.@-_^°]", RegexOptions.None, TimeSpan.FromSeconds(1.5));

        [ThreadStatic]
        private static char[]? sanitizedShared;
        public static unsafe string Sanitize(this string? value, HashSet<char>? illegal = null, bool space = false) {
            const int buffer = 64;

            if (value.IsNullOrEmpty())
                return "";

            char[] sanitizedArray = sanitizedShared ?? new char[value.Length + buffer];
            if (sanitizedArray.Length < value.Length)
                sanitizedArray = new char[value.Length + buffer];
            sanitizedShared = sanitizedArray;

            fixed (char* sanitized = sanitizedArray)
            fixed (char* raw = value) {
                char* to = sanitized;
                char* last = (char*) IntPtr.Zero;
                char* from = raw;
                if (illegal != null && illegal.Count != 0) {
                    if (!space) {
                        for (int i = value.Length; i > 0; --i) {
                            char c = *from++;
                            if (char.IsWhiteSpace(c))
                                continue;
                            else
                                last = to;
                            if (!EnglishFontCharsSet.Contains(c))
                                continue;
                            if (illegal.Contains(c))
                                continue;
                            *to++ = c;
                        }
                    } else {
                        bool isStart = true;
                        for (int i = value.Length; i > 0; --i) {
                            char c = *from++;
                            if (isStart && char.IsWhiteSpace(c))
                                continue;
                            else
                                last = to;
                            isStart = false;
                            if (!EnglishFontCharsSet.Contains(c))
                                continue;
                            if (illegal.Contains(c))
                                continue;
                            *to++ = c;
                        }
                    }

                } else {
                     if (!space) {
                        for (int i = value.Length; i > 0; --i) {
                            char c = *from++;
                            if (char.IsWhiteSpace(c))
                                continue;
                            else
                                last = to;
                            if (!EnglishFontCharsSet.Contains(c))
                                continue;
                            *to++ = c;
                        }
                    } else {
                        bool isStart = true;
                        for (int i = value.Length; i > 0; --i) {
                            char c = *from++;
                            if (isStart && char.IsWhiteSpace(c))
                                continue;
                            else
                                last = to;
                            isStart = false;
                            if (!EnglishFontCharsSet.Contains(c))
                                continue;
                            *to++ = c;
                        }
                    }
                }

                if (last == (char*) IntPtr.Zero)
                    return "";

                return StringDedupeStaticContext.ToDedupedString(sanitized, (int) (last - sanitized) + 1);
            }
        }

        public static T Await<T>(this Task<T> task) {
            T result = default;
            task.ContinueWith(_ => result = task.Result).Wait();
            if (result is null)
                throw new NullReferenceException("Task returned null: " + task);
            return result;
        }

        public static byte[] ToBytes(this Stream stream) {
            using (MemoryStream ms = new MemoryStream()) {
                stream.CopyTo(ms);
                long length = ms.Position;
                ms.Seek(0, SeekOrigin.Begin);
                byte[] buffer = ms.GetBuffer();
                if (buffer.Length != length)
                    buffer = ms.ToArray();
                return buffer;
            }
        }

    }
}
