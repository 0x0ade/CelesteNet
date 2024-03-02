using Monocle;

namespace Celeste.Mod.CelesteNet.Client.Entities
{
    public class PauseUpdateOverlay : Overlay {

        public override void Update() {
            base.Update();

            foreach (Entity e in Engine.Scene[Tags.PauseUpdate])
                if (e.Active && e is not TextMenu)
                    e.Update();
        }

    }
}
