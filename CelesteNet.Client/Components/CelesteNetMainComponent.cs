using Celeste.Mod.CelesteNet.Client.Entities;
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

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetMainComponent : CelesteNetGameComponent {

        private DataPlayerState LastState;
        private Player Player;
        private Session Session;

        public Dictionary<uint, Ghost> Ghosts = new Dictionary<uint, Ghost>();

        public CelesteNetMainComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10100;
            Visible = false;
        }

        public override void Start() {
            base.Start();

            On.Celeste.Level.LoadLevel += OnLoadLevel;
            Everest.Events.Level.OnExit += OnExitLevel;

            if (Engine.Instance != null && Engine.Scene is Level level)
                OnLoadLevel(null, level, Player.IntroTypes.Transition, true);
        }

        public void Handle(CelesteNetConnection con, DataPlayerFrame frame) {
            if (!(Engine.Scene is Level level))
                return;

            if (!Ghosts.TryGetValue(frame.Player.ID, out Ghost ghost) ||
                ghost.Scene != Engine.Scene ||
                ghost.Sprite.Mode != frame.SpriteMode) {
                ghost?.RemoveSelf();
                Ghosts.Remove(frame.Player.ID);
            }

            if (!Client.Data.TryGetBoundRef(frame.Player, out DataPlayerState state) ||
                state.SID != Session.Area.SID)
                return;

            level.Add(Ghosts[frame.Player.ID] = ghost = new Ghost(frame.SpriteMode));

            ghost.NameTag.Name = frame.Player.FullName;
            ghost.UpdateSprite(frame.Position, frame.Scale, frame.Facing, frame.Color, frame.SpriteRate, frame.SpriteJustify, frame.CurrentAnimationID, frame.CurrentAnimationFrame);
            ghost.UpdateHair(frame.Facing, frame.HairColor, frame.HairSimulateMotion, frame.HairCount, frame.HairColors, frame.HairTextures);
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (Client == null || !Client.IsReady)
                return;

            if (!(Engine.Scene is Level level))
                return;

            if (Player == null || Player.Scene != Engine.Scene)
                Player = level.Tracker.GetEntity<Player>();
            if (Player == null)
                return;

            SendFrame();
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            try {
                On.Celeste.Level.LoadLevel -= OnLoadLevel;
                Everest.Events.Level.OnExit -= OnExitLevel;
            } catch (InvalidOperationException) {
            }

            Cleanup();
        }

        public void Cleanup() {
            Player = null;
            Session = null;

            foreach (Ghost ghost in Ghosts.Values)
                ghost?.RemoveSelf();
            Ghosts.Clear();
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

        public void OnExitLevel(Level level, LevelExit exit, LevelExit.Mode mode, Session session, HiresSnow snow) {
            Session = null;

            Cleanup();

            SendState();
        }

        #endregion


        #region Send

        public void SendState() {
            Client?.SendAndHandle(LastState = new DataPlayerState {
                ID = Client.PlayerInfo.ID,
                SID = Session?.Area.GetSID() ?? "",
                Mode = Session?.Area.Mode ?? AreaMode.Normal,
                Level = Session?.Level ?? "",
                Idle = Player?.Scene?.Paused ?? false
            });
        }

        public void SendFrame() {
            if (Player == null || Player.Sprite == null || Player.Hair == null)
                return;

            int hairCount = Player.Sprite.HairCount;
            Color[] hairColors = new Color[hairCount];
            for (int i = 0; i < hairCount; i++)
                hairColors[i] = Player.Hair.GetHairColor(i);
            string[] hairTextures = new string[hairCount];
            for (int i = 0; i < hairCount; i++)
                hairTextures[i] = Player.Hair.GetHairTexture(i).AtlasPath;

            Client?.Send(new DataPlayerFrame {
                Position = Player.Position,
                Speed = Player.Speed,
                Scale = Player.Sprite.Scale,
                Color = Player.Sprite.Color,
                Facing = Player.Facing,

                SpriteMode = Player.Sprite.Mode,
                CurrentAnimationID = Player.Sprite.CurrentAnimationID,
                CurrentAnimationFrame = Player.Sprite.CurrentAnimationFrame,

                HairColor = Player.Hair.Color,
                HairSimulateMotion = Player.Hair.SimulateMotion,

                HairCount = (byte) hairCount,
                HairColors = hairColors,
                HairTextures = hairTextures,

                DashColor = Player.StateMachine.State == Player.StDash ? Player.GetCurrentTrailColor() : (Color?) null,
                DashDir = Player.DashDir,
                DashWasB = Player.GetWasDashB()
            });
        }

        #endregion

    }
}
