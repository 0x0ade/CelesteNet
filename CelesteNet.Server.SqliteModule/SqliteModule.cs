namespace Celeste.Mod.CelesteNet.Server.Sqlite
{
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
