namespace Celeste.Mod.CelesteNet.Server.Sqlite
{
    public class SqliteSettings : CelesteNetServerModuleSettings {

        public bool Enabled { get; set; } = true;

        public string UserDataRoot { get; set; } = "UserData";

    }
}
