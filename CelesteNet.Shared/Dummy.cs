using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet
{
    public static class Dummy<T> {

        public static readonly T[] EmptyArray = new T[0];
        public static readonly List<T> EmptyList = new();
#pragma warning disable CS8601 // default? isn't a thing, T? requires a constrained T.
        public static readonly T Default = default;
#pragma warning restore CS8601

    }
}
