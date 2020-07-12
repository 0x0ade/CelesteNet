using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Client.Entities {
    public class GhostFollower : GhostEntity {

        public Follower Follower;

        public GhostFollower(Ghost ghost)
            : base(ghost) {
            Add(Follower = new Follower());
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
