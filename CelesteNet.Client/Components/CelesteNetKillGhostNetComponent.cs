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
        public FieldInfo m_GhostNetModule_Client;

        public CelesteNetKillGhostNetComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;

            foreach (EverestModule module in Everest.Modules) {
                if (module.GetType().FullName != "Celeste.Mod.Ghost.Net.GhostNetModule")
                    continue;

                GhostNetModule = module;
                m_GhostNetModule_Client = module.GetType().GetField("Client");
            }
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (m_GhostNetModule_Client?.GetValue(GhostNetModule) != null) {
                Context.Status.Set("Disconnected from CelesteNet, connected to GhostNet", 10, false);
                Settings.Connected = false;
            }
        }

    }
}
