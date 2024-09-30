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
        private float TickDelay = 0f;

        public CelesteNetTickerComponent(CelesteNetClientContext context, Game game) : base(context, game) {}

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (TickRate <= 0 || !(Client?.IsReady ?? false))
                return;

            TickDelay -= Engine.RawDeltaTime;
            if (TickRate >= 60 || TickDelay <= 0) {
                TickDelay += 1 / TickRate;
                if (TickDelay < MinTickDelay)
                    TickDelay = MinTickDelay;

                // Tick components
                if (Context != null)
                    foreach (ITickReceiver receiver in Context.Components.Values)
                        receiver.Tick();

                if (Engine.Scene?.Tracker != null) {
                    // Tick all tracked entities and components which implement the interface
                    foreach (KeyValuePair<Type, List<Entity>> ets in Engine.Scene.Tracker.Entities) {
                        if (typeof(ITickReceiver).IsAssignableFrom(ets.Key)) {
                            foreach (Entity et in ets.Value)
                                ((ITickReceiver) et).Tick();
                        }
                    }

                    foreach (KeyValuePair<Type, List<Component>> cps in Engine.Scene.Tracker.Components) {
                        if (typeof(ITickReceiver).IsAssignableFrom(cps.Key)) {
                            foreach (Component cp in cps.Value)
                                ((ITickReceiver) cp).Tick();
                        }
                    }
                }

                OnTick?.Invoke();
            }
        }

        public void Handle(CelesteNetConnection con, DataTickRate rate) {
            TickRate = rate.TickRate;
            Logger.Log(LogLevel.INF, "client-ticker", $"Changed tick rate to {rate.TickRate} TpS");
        }

        public event Action? OnTick;

    }
}