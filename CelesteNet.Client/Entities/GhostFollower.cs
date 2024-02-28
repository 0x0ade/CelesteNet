using Monocle;

namespace Celeste.Mod.CelesteNet.Client.Entities
{
    public class GhostFollower : GhostEntity {

        public Follower Follower;

        public GhostFollower(Ghost ghost)
            : base(ghost) {
            Add(Follower = new());
        }

        public override void Added(Scene scene) {
            base.Added(scene);
            Ghost?.Leader?.GainFollower(Follower);
        }

        public override void Removed(Scene scene) {
            Follower.Leader?.LoseFollower(Follower);
            base.Removed(scene);
        }

    }
}
