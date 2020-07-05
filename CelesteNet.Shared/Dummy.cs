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
    public static class Dummy<T> {

        public static readonly T[] EmptyArray = new T[0];
        public static readonly List<T> EmptyList = new List<T>();
#pragma warning disable CS8601 // default? isn't a thing, T? requires a constrained T.
        public static readonly T Default = default;
#pragma warning restore CS8601

    }
}
