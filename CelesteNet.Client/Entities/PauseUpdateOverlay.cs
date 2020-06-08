using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    public class PauseUpdateOverlay : Overlay {

        public override void Update() {
            base.Update();

            foreach (Entity e in Engine.Scene[Tags.PauseUpdate])
                if (e.Active && !(e is TextMenu))
                    e.Update();
        }

    }
}
