using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class FrontendSettings {

        // Make sure to update your index.html as well!
        public string CanonicalRoot { get; set; } = "https://celestenet.0x0ade.ga/";

        public int Port { get; set; } = 3800;
        public string Password { get; set; } = "actuallyHosts";

        public string ContentRoot { get; set; } = "Content";

    }
}
