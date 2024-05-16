using System.IO;

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

            Logger.Log(LogLevel.VVV, "sqliteModule", $"UpdateUserData {active} {enabled} {Settings.RunSelfTests}");

            if (enabled && Settings.RunSelfTests) {
                RunSelfTests();
            }

            if (!active && enabled) {
                Server.UserData.Dispose();
                Server.UserData = new SqliteUserData(this);

            } else if (active && !enabled) {
                Server.UserData.Dispose();
                Server.UserData = new FileSystemUserData(Server);
            }
        }

        private void RunSelfTests() {
            Logger.Log(LogLevel.VVV, "sqliteModule", $"Start self tests");

            string selftestDB = "selftest.db";

            string selftestDBPath = Path.Combine(Settings.UserDataRoot, selftestDB);

            if (File.Exists(selftestDBPath)) {
                Logger.Log(LogLevel.INF, "sqlite", $"... Deleting old {selftestDBPath} ...");
                File.Delete(selftestDBPath);
            }

            using (SqliteUserData selfTestUserData = new SqliteUserData(this, null, selftestDB)) {
                Logger.Log(LogLevel.INF, "sqlite", $"\n====\n Running basic SQLiteModule Self Tests on {selfTestUserData.DBPath}...\n====\n");

                selfTestUserData.RunSelfTests();

                Logger.Log(LogLevel.INF, "sqlite", "\n====\n Finished basic SQLiteModule Self Tests.\n====\n");
            }
        }
    }
}
