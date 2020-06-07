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

namespace Celeste.Mod.CelesteNet.Client {
    public static class CelesteNetClientUtils {

        private readonly static FieldInfo f_Player_wasDashB =
            typeof(Player).GetField("wasDashB", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool GetWasDashB(this Player self)
            => (bool) f_Player_wasDashB.GetValue(self);

    }
}
