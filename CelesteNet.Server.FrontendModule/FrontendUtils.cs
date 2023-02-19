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
                Text = $"[{msg.Date.ToLocalTime().ToLongTimeString()}] {msg.ToString(false, false)}"
            };

        public static object ToDetailedFrontendChat(this DataChat msg)
            => new {
                msg.ID,
                PlayerID = msg.Player?.ID ?? uint.MaxValue,
                Targets = msg.Targets?.Select(p => p?.ID ?? uint.MaxValue) ?? null,
                Color = msg.Color.ToHex(),
                DateTime = msg.Date.ToUnixTimeMillis(),
                msg.Tag,
                msg.Text
            };

        private static readonly DateTime UnixTimeStart = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static double ToUnixTimeMillis(this DateTime time)
            => time.Subtract(UnixTimeStart).TotalMilliseconds;

    }
}
