using Microsoft.Xna.Framework;
using System.Reflection;

namespace Celeste.Mod.CelesteNet.Client.Components
{
    public class CelesteNetKillGhostNetComponent : CelesteNetGameComponent {

        public object GhostNetModule;
        public FieldInfo m_GhostNetModule_Client;

        public CelesteNetKillGhostNetComponent(CelesteNetClientContext context, Game game)
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
