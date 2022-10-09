using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.Helpers;
using Monocle;

namespace Celeste.Mod.CelesteNet {

    public class CommandInfo {
        public string ID = "";
        public string AliasTo = "";
        public bool Auth = false;
        public bool AuthExec = false;
        public CompletionType FirstArg = CompletionType.None;
    }

    public enum CompletionType : byte {
        None = 0,
        Command = 1,
        Channel = 2,
        Player = 3
    }
}
