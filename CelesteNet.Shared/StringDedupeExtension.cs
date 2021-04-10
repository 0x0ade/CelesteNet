using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectFactories;

namespace Celeste.Mod.CelesteNet {
    // Because String.Intern is too global for my liking. -jade
    public static unsafe class StringDedupeExtension {

        public class DedupeInfo {
            public int Refs;
            public readonly List<string> Strings = new List<string>();
        }

        public static int MinimumRefs = 32;

        [ThreadStatic]
        private static Dictionary<int, DedupeInfo>? _Map;
        public static Dictionary<int, DedupeInfo> Map => _Map ??= new Dictionary<int, DedupeInfo>();

        private static int GetHash(char* ptr, int length) {
            // Based off of .NET reference code.

            int hash1 = 5381;
            int hash2 = hash1;

            int* pint = (int*) ptr;
            int len = length;
            while (len > 2) {
                hash1 = ((hash1 << 5) + hash1 + (hash1 >> 27)) ^ pint[0];
                hash2 = ((hash2 << 5) + hash2 + (hash2 >> 27)) ^ pint[1];
                pint += 2;
                len  -= 4;
            }

            if (len > 0) {
                hash1 = ((hash1 << 5) + hash1 + (hash1 >> 27)) ^ pint[0];
            }

            return hash1 + (hash2 * 1566083941);
        }

        private static bool Equals(char* ptr1, string other) {
            fixed (char* ptr2 = other) {
                int length = other.Length;
                int ptrSize = IntPtr.Size;
                int lengthBounded = length - (length % ptrSize);
                int i = 0;
                for (; i < lengthBounded; i += ptrSize)
                    if (*(IntPtr*) ((long) ptr1 + i) !=
                        *(IntPtr*) ((long) ptr2 + i))
                        return false;
                for (; i < length; i += sizeof(char))
                    if (*(char*) ((long) ptr1 + i) !=
                        *(char*) ((long) ptr2 + i))
                        return false;
                return true;
            }
        }

        public static string Dedupe(this string value) {
            int hash;
            fixed (char* ptr = value)
                hash = GetHash(ptr, value.Length);

            Dictionary<int, DedupeInfo> hashes = Map;
            if (!hashes.TryGetValue(hash, out DedupeInfo? dedupe))
                hashes[hash] = dedupe = new DedupeInfo();

            foreach (string other in dedupe.Strings)
                if (other == value)
                    return other;

            if (++dedupe.Refs >= MinimumRefs)
                dedupe.Strings.Add(value);
            return value;
        }

        public static string ToDedupedString(this char[] chars, int count) {
            fixed (char* ptr = chars) {
                int hash = GetHash(ptr, count);
                string value;

                Dictionary<int, DedupeInfo> hashes = Map;
                if (!hashes.TryGetValue(hash, out DedupeInfo? dedupe))
                    hashes[hash] = dedupe = new DedupeInfo();

                foreach (string other in dedupe.Strings)
                    if (count == other.Length && Equals(ptr, other))
                        return other;

                value = new string(ptr, 0, count);
                if (++dedupe.Refs >= MinimumRefs)
                    dedupe.Strings.Add(value);
                return value;
            }
        }

    }
}
