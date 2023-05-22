using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public static class ColorHelpers {

        // move the Calc.HexToColor functions outside Monocle.Calc
        // to remove the Celeste dependency from the chat module
        public static Color HexToColor(uint rgb) {
            Color result = default;
            result.A = (byte)(rgb >> 24);
            result.R = (byte)(rgb >> 16);
            result.G = (byte)(rgb >> 8);
            result.B = (byte)rgb;
            return result;
        }

        public static Color HexToColor(string hex) {
            int offset = hex.Length >= 1 && hex[0] == '#' ? 1 : 0;

            if (hex.Length - offset < 6)
                throw new ArgumentException($"The provided color ({hex}) is invalid.");

            try
            {
                int r = int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber);
                int g = int.Parse(hex.Substring(offset + 2, 2), NumberStyles.HexNumber);
                int b = int.Parse(hex.Substring(offset + 4, 2), NumberStyles.HexNumber);
                return new(r, g, b);
            } catch (FormatException e) {
                throw new ArgumentException($"The provided color ({hex}) is invalid.", e);
            }
        }

        public static byte HexToByte(char c)
        {
            return (byte)"0123456789ABCDEF".IndexOf(char.ToUpper(c));
        }
    }
}
