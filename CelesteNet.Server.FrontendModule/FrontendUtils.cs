using Celeste.Mod.CelesteNet.DataTypes;
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

namespace Celeste.Mod.CelesteNet.Server.Control {
    public static class FrontendUtils {

        public static object ToFrontendChat(this DataChat msg)
            => new {
                msg.ID,
                PlayerID = msg.Player?.ID ?? uint.MaxValue,
                Color = msg.Color.ToHex(),
                Text = msg.ToString()
            };

    }
}
