using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetMainComponent : CelesteNetGameComponent {

        private DataPlayerState LastState;
        private Player Player;
        private Session Session;

        public CelesteNetMainComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10100;
        }

        public override void Start() {
            base.Start();

            On.Celeste.Level.LoadLevel += OnLoadLevel;
            if (Engine.Instance != null && Engine.Scene is Level level)
                OnLoadLevel(null, level, Player.IntroTypes.Transition, true);
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            

        }

        public override void Draw(GameTime gameTime) {
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            try {
                On.Celeste.Level.LoadLevel -= OnLoadLevel;
            } catch (InvalidOperationException) {
            }
        }


        #region Hooks

        public void OnLoadLevel(On.Celeste.Level.orig_LoadLevel orig, Level level, Player.IntroTypes playerIntro, bool isFromLoader = false) {
            orig?.Invoke(level, playerIntro, isFromLoader);

            Session = level.Session;

            if (Client == null)
                return;

            Player = level.Tracker.GetEntity<Player>();

            SendState();
        }

        #endregion


        #region Send

        public void SendState() {
            Client.SendAndSet(LastState = new DataPlayerState {
                SID = Session?.Area.GetSID() ?? "",
                Mode = Session?.Area.Mode ?? AreaMode.Normal,
                Level = Session?.Level ?? "",
                Idle = Player?.Scene?.Paused ?? false
            });
        }

        #endregion

    }
}
