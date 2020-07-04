using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetKillGhostNetComponent : CelesteNetGameComponent {

        public object GhostNetModule;
        public MethodInfo m_GhostNetModule_Stop;

        public CelesteNetKillGhostNetComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;

            foreach (EverestModule module in Everest.Modules) {
                if (module.GetType().FullName != "Celeste.Mod.Ghost.Net.GhostNetModule")
                    continue;

                GhostNetModule = module;
                m_GhostNetModule_Stop = module.GetType().GetMethod("Stop");
            }
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (m_GhostNetModule_Stop != null)
                m_GhostNetModule_Stop.Invoke(GhostNetModule, Dummy<object>.EmptyArray);
        }

    }
}
