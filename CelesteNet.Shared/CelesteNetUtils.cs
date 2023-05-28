using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet {
    public static partial class CelesteNetUtils {

        public const ushort Version = 2;
        public static readonly ushort LoadedVersion = Version;

        // Port MUST be fixed as the website expects it to be the same for everyone.
        public static readonly int ClientRCPort = 38038;

        public static readonly Encoding UTF8NoBOM = new UTF8Encoding(false);

        // See https://github.com/dotnet/runtime/blob/144e5145453ac3885ac20bc1f1f2641523c6fcea/src/libraries/System.Private.CoreLib/src/System/String.cs#L488
        public static bool IsNullOrEmpty([NotNullWhen(false)] this string? value)
            => string.IsNullOrEmpty(value);

        public static string? Nullify(this string? value)
            => string.IsNullOrEmpty(value) ? null : value;

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

        private static IEnumerable<Assembly> _GetAssemblies()
            => AppDomain.CurrentDomain.GetAssemblies().Distinct();

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

        [ThreadStatic]
        private static char[]? sanitizedShared;
        public static unsafe string Sanitize(this string? value, HashSet<char>? illegal = null, bool space = false, bool dedupe = true) {
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
                            if (illegal.Contains(c))
                                continue;
                            if (!EnglishFontCharsSet.Contains(c))
                                continue;
                            if (char.IsWhiteSpace(c))
                                continue;
                            else
                                last = to;
                            *to++ = c;
                        }
                    } else {
                        bool isStart = true;
                        for (int i = value.Length; i > 0; --i) {
                            char c = *from++;
                            if (illegal.Contains(c))
                                continue;
                            if (!EnglishFontCharsSet.Contains(c))
                                continue;
                            if (isStart && char.IsWhiteSpace(c))
                                continue;
                            else
                                last = to;
                            isStart = false;
                            *to++ = c;
                        }
                    }

                } else {
                    if (!space) {
                        for (int i = value.Length; i > 0; --i) {
                            char c = *from++;
                            if (!EnglishFontCharsSet.Contains(c))
                                continue;
                            if (char.IsWhiteSpace(c))
                                continue;
                            else
                                last = to;
                            *to++ = c;
                        }
                    } else {
                        bool isStart = true;
                        for (int i = value.Length; i > 0; --i) {
                            char c = *from++;
                            if (!EnglishFontCharsSet.Contains(c))
                                continue;
                            if (isStart && char.IsWhiteSpace(c))
                                continue;
                            else
                                last = to;
                            isStart = false;
                            *to++ = c;
                        }
                    }
                }

                if (last == (char*) IntPtr.Zero)
                    return "";

                int count = (int) (last - sanitized) + 1;

                return !dedupe ? new(sanitized, 0, count) : StringDedupeStaticContext.ToDedupedString(sanitized, count);
            }
        }

        public static T Await<T>(this Task<T> task) {
            T? result = default;
            task.ContinueWith(_ => result = task.Result).Wait();
            if (result is null)
                throw new NullReferenceException("Task returned null: " + task);
            return result;
        }

        public static byte[] ToBytes(this Stream stream) {
            if (stream is MemoryStream ms)
                return ms.ToArray();

            long length;
            if (stream.CanSeek) {
                try {
                    length = stream.Length - stream.Position;
                } catch {
                    length = 0;
                }
            } else {
                length = 0;
            }

            if (length != 0) {
                byte[] data = new byte[length];
                using (ms = new(data, 0, (int) length, true, true)) {
                    stream.CopyTo(ms);
                }
                return data;
            }

            using (ms = new()) {
                stream.CopyTo(ms);
                length = ms.Position;
                ms.Seek(0, SeekOrigin.Begin);
                byte[] buffer = ms.GetBuffer();
                if (buffer.Length != length)
                    buffer = ms.ToArray();
                return buffer;
            }
        }

        public static void ShutdownSafe(this Socket sock, SocketShutdown shutdown) {
            if (!sock.Connected)
                return;
            try {
                // On Unix, we're supposed to shut down the socket and / or signal the recving thread.
                // On Windows, we might need to set the recv timeout to 0 to properly cancel pending recvs.
                switch (shutdown) {
                    case SocketShutdown.Receive:
                    case SocketShutdown.Both:
                        try {
                            sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 0);
                        } catch {
                            // Not all platforms like this.
                        }
                        break;
                }
                sock.Shutdown(shutdown);
            } catch (SocketException se) {
                // Sometime the first check isn't enough
                if (se.IsDisconnect())
                    return;
                throw;
            }
        }

        public static bool IsDisconnect(this SocketException se) {
            switch (se.SocketErrorCode) {
                case SocketError.Shutdown:
                case SocketError.NotConnected:
                case SocketError.ConnectionRefused:
                case SocketError.ConnectionAborted:
                case SocketError.ConnectionReset:
                    return true;
                default:
                    return false;
            }
        }

    }

    public static class NetStringCtrl {

        public const char End = '\x00';

        public const char Mapped = '\x01';

        public const char Repeat = '\x02';
        public const string RepeatString = "\x02";

        public const char First = '\x01';
        public const char ReservedFirst = '\x03';
        public const char FreeFirst = '\x10';
        public const char Last = '\x1f';

        public static readonly string[] Strings = _GenerateStrings();
        private static string[] _GenerateStrings() {
            string[] all = new string[0x20];
            for (int i = 0; i < all.Length; i++)
                all[i] = "" + (char) (byte) i;

            // Special cases to make use of interned strings.
            all[Repeat] = RepeatString;

            return all;
        }

    }
}
