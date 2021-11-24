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

        public const float MinTickDelay = 0.01f;

        public volatile float TickRate = 0f;
        private float tickDelay = 0f;

        public CelesteNetTickerComponent(CelesteNetClientContext context, Game game) : base(context, game) {}

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (TickRate <= 0)
                return;

            tickDelay -= Engine.RawDeltaTime;
            if (TickRate >= 60 || tickDelay <= 0) {
                tickDelay += 1 / TickRate;
                if (tickDelay < MinTickDelay)
                    tickDelay = MinTickDelay;

                // Tick components
                foreach (ITickReceiver receiver in Context.Components.Values)
                    receiver.Tick();

                if (Engine.Scene?.Tracker != null) {
                    // Tick all tracked entities and components which implement the interface
                    foreach (var ets in Engine.Scene.Tracker.Entities) {
                        if (typeof(ITickReceiver).IsAssignableFrom(ets.Key)) {
                            foreach (Entity et in ets.Value)
                                ((ITickReceiver) et).Tick();
                        }
                    }

                    foreach (var cps in Engine.Scene.Tracker.Components) {
                        if (typeof(ITickReceiver).IsAssignableFrom(cps.Key)) {
                            foreach (Component cp in cps.Value)
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