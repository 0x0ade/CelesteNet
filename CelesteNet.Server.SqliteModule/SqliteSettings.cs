namespace Celeste.Mod.CelesteNet.Server.Sqlite
{
    public class SqliteSettings : CelesteNetServerModuleSettings {

        public bool Enabled { get; set; } = true;

        public bool RunSelfTests { get; set; } = false;

        public string UserDataRoot { get; set; } = "UserData";

    }
}
