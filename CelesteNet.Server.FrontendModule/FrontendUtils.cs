using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Linq;

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
                Name = msg.Player?.FullName ?? string.Empty,
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
