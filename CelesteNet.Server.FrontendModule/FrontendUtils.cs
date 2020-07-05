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
                Targeted = msg.Targets != null,
                Color = msg.Color.ToHex(),
                Text = msg.ToString(false, false)
            };

        public static double ToUnixTime(this DateTime time)
            => time.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

    }
}
