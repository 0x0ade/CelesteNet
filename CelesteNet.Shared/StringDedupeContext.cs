using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectFactories;

namespace Celeste.Mod.CelesteNet {
    // Because String.Intern is too global for my liking. -jade
    public unsafe class StringDedupeContext {

        public class DedupeInfo {
            public int Refs;
            public readonly List<string> Strings = new List<string>();
        }

        public readonly Dictionary<int, DedupeInfo> Map = new Dictionary<int, DedupeInfo>();

        private readonly Dictionary<int, int> Counting = new Dictionary<int, int>();
        private readonly List<int> Uncounted = new List<int>();

        public int PromotionCount = 18;
        public int PromotionTreshold = 10;
        public int MaxCounting = 32;
        public int DemotionScore = 3;
        public int MinLength = 4;
        public int MaxLength = 4096;

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

        public void Count(int value) {
            if (Map.ContainsKey(value))
                return;

            if (!Counting.TryGetValue(value, out int count))
                count = 0;
            if (++count >= PromotionCount) {
                Counting.Remove(value);
                Map[value] = new DedupeInfo();
            } else {
                Counting[value] = count;
            }

            if (Counting.Count >= MaxCounting)
                Cleanup();
        }

        public void Cleanup() {
            foreach (KeyValuePair<int, int> entry in Counting) {
                int score = entry.Value - DemotionScore;
                if (score <= 0) {
                    Uncounted.Add(entry.Key);
                } else if (score >= PromotionTreshold) {
                    Uncounted.Add(entry.Key);
                    Map[entry.Key] = new DedupeInfo();
                } else {
                    Counting[entry.Key] = score;
                }
            }
            foreach (int value in Uncounted)
                Counting.Remove(value);
            Uncounted.Clear();
        }

        public string Dedupe(string value) {
            if (value.Length == 0)
                return "";

            if (value.Length < MinLength ||
                value.Length > MaxLength)
                return value;

            int hash;
            fixed (char* ptr = value)
                hash = GetHash(ptr, value.Length);

            if (Map.TryGetValue(hash, out DedupeInfo? dedupe)) {
                dedupe.Refs++;

                foreach (string other in dedupe.Strings)
                    if (other == value)
                        return other;

                dedupe.Strings.Add(value);
                return value;
            }

            Count(hash);
            return value;
        }

        public string ToDedupedString(char[] chars, int count) {
            if (count == 0)
                return "";

            if (count < MinLength ||
                count > MaxLength)
                return new string(chars, 0, count);

            fixed (char* ptr = chars) {
                return ToDedupedString(ptr, count);
            }
        }

        public string ToDedupedString(char* chars, int count) {
            if (count == 0)
                return "";

            if (count < MinLength ||
                count > MaxLength)
                return new string(chars, 0, count);

            int hash = GetHash(chars, count);

            if (Map.TryGetValue(hash, out DedupeInfo? dedupe)) {
                dedupe.Refs++;

                foreach (string other in dedupe.Strings)
                    if (count == other.Length && Equals(chars, other))
                        return other;

                string value = new string(chars, 0, count);
                dedupe.Strings.Add(value);
                return value;
            }

            Count(hash);
            return new string(chars, 0, count);
        }

    }

    public static unsafe class StringDedupeStaticContext {

        [ThreadStatic]
        private static StringDedupeContext? _Ctx;
        public static StringDedupeContext Ctx => _Ctx ??= new StringDedupeContext();

        public static string Dedupe(this string value)
            => Ctx.Dedupe(value);

        public static string ToDedupedString(this char[] chars, int count)
            => Ctx.ToDedupedString(chars, count);

        public static string ToDedupedString(char* chars, int count)
            => Ctx.ToDedupedString(chars, count);

    }
}
