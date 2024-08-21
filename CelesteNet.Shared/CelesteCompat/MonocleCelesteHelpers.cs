using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.CelesteNet.MonocleCelesteHelpers {
    public enum AreaMode {
        Normal,
        BSide,
        CSide
    }

    public enum Facings {
        Right = 1,
        Left = -1
    }

    public enum PlayerSpriteMode {
        Madeline,
        MadelineNoBackpack,
        Badeline,
        MadelineAsBadeline,
        Playback
    }

    public static class CalcHelpers {

        public static int Clamp(int value, int min, int max) {
            return Math.Min(Math.Max(value, min), max);
        }

        public static float Clamp(float value, float min, float max) {
            return Math.Min(Math.Max(value, min), max);
        }

        public static Vector2 AngleToVector(float angleRadians, float length) {
            return new Vector2((float)Math.Cos(angleRadians) * length, (float)Math.Sin(angleRadians) * length);
        }

        public static float Angle(this Vector2 vector) {
            return (float)Math.Atan2(vector.Y, vector.X);
        }

        public static T Choose<T>(this Random random, params T[] choices) {
            return choices[random.Next(choices.Length)];
        }
    }

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

            try {
                int r = int.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber);
                int g = int.Parse(hex.Substring(offset + 2, 2), NumberStyles.HexNumber);
                int b = int.Parse(hex.Substring(offset + 4, 2), NumberStyles.HexNumber);
                return new(r, g, b);
            } catch (FormatException e) {
                throw new ArgumentException($"The provided color ({hex}) is invalid.", e);
            }
        }

        public static byte HexToByte(char c) {
            return (byte)"0123456789ABCDEF".IndexOf(char.ToUpper(c));
        }
    }

    public static class InvokeHelpers {
        //
        // Summary:
        //     Invokes all delegates in the invocation list, as long as the previously invoked
        //     delegate returns true.
        public static bool InvokeWhileTrue(this MulticastDelegate md, params object[] args) {
            if (md == null) {
                return true;
            }

            Delegate[] invocationList = md.GetInvocationList();
            for (int i = 0; i < invocationList.Length; i++) {
                if (!(bool)invocationList[i].DynamicInvoke(args)) {
                    return false;
                }
            }

            return true;
        }
    }
}
