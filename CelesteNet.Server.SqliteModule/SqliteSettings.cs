using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Sqlite {
    public class SqliteSettings : CelesteNetServerModuleSettings {

        public bool Enabled { get; set; } = true;

        public string UserDataRoot { get; set; } = "UserData";

    }
}
