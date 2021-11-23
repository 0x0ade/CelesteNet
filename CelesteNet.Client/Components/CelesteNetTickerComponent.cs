using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Client.Components {
    // All Monocle entities/component which implement this must be tracked
    public interface ITickReceiver {
        void Tick();
    }

    public class CelesteNetTickerComponent : CelesteNetGameComponent {

        public volatile float TickRate = 0f;
        private float tickDelay = 0f;

        public CelesteNetTickerComponent(CelesteNetClientContext context, Game game) : base(context, game) {}

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (TickRate <= 0)
                return;

            tickDelay -= Engine.RawDeltaTime;
            if (tickDelay <= 0) {
                tickDelay = 1 / TickRate;

                // Tick components
                foreach (ITickReceiver receiver in Context.Components.Values)
                    receiver.Tick();

                if (Engine.Scene?.Tracker != null) {
                    // Tick all tracked entities and components which implement the interface
                    foreach ((Type type, List<Entity> entities) in Engine.Scene.Tracker.Entities) {
                        if (typeof(ITickReceiver).IsAssignableFrom(type)) {
                            foreach (Entity et in entities)
                                ((ITickReceiver) et).Tick(); 
                        }
                    }

                    foreach ((Type type, List<Component> components) in Engine.Scene.Tracker.Components) {
                        if (typeof(ITickReceiver).IsAssignableFrom(type)) {
                            foreach (Component cp in components)
                                ((ITickReceiver) cp).Tick(); 
                        }
                    }
                }
            }
        }

        public void Handle(CelesteNetConnection con, DataTickRate rate) {
            TickRate = rate.TickRate;
            Logger.Log(LogLevel.INF, "client-ticker", $"Changed tick rate to {rate.TickRate} TpS");
        }

    }
}