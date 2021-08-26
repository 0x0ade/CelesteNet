using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Sqlite {
    public class SqliteModule : CelesteNetServerModule<SqliteSettings> {

        public override void LoadSettings() {
            base.LoadSettings();

            UpdateUserData(Settings.Enabled);
        }

        public override void SaveSettings() {
            base.SaveSettings();

            UpdateUserData(Settings.Enabled);
        }

        public override void Dispose() {
            base.Dispose();

            UpdateUserData(false);
        }

        public void UpdateUserData(bool enabled) {
            bool active = Server.UserData is SqliteUserData;

            if (!active && enabled) {
                Server.UserData.Dispose();
                Server.UserData = new SqliteUserData(this);

            } else if (active && !enabled) {
                Server.UserData.Dispose();
                Server.UserData = new FileSystemUserData(Server);
            }
        }

    }
}
