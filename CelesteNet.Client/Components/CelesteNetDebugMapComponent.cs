using Celeste.Editor;
using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetDebugMapComponent : CelesteNetGameComponent {

        private static readonly FieldInfo f_MapEditor_area =
            typeof(MapEditor)
            .GetField("area", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo f_MapEditor_Camera =
            typeof(MapEditor)
            .GetField("Camera", BindingFlags.NonPublic | BindingFlags.Static);

        private AreaKey? LastArea;

        public Dictionary<uint, DebugMapGhost> Ghosts = new Dictionary<uint, DebugMapGhost>();

        public CelesteNetDebugMapComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10300;
            Visible = false;
        }

        public override void Initialize() {
            base.Initialize();

            MainThreadHelper.Do(() => {
                On.Celeste.Editor.MapEditor.Render += OnMapEditorRender;
            });
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            MainThreadHelper.Do(() => {
                On.Celeste.Editor.MapEditor.Render -= OnMapEditorRender;
            });

            Cleanup();
        }

        public void Cleanup() {
            lock (Ghosts)
                if (Ghosts.Count > 0)
                    Ghosts.Clear();
        }

        #region Handlers

        public void Handle(CelesteNetConnection con, DataPlayerInfo player) {
            if (player.ID == Client.PlayerInfo.ID || LastArea == null)
                return;

            lock (Ghosts)
                if (Ghosts.TryGetValue(player.ID, out DebugMapGhost ghost) &&
                    string.IsNullOrEmpty(player.DisplayName))
                    Ghosts.Remove(player.ID);
        }

        public void Handle(CelesteNetConnection con, DataChannelMove move) {
            if (LastArea == null)
                return;

            if (move.Player.ID == Client.PlayerInfo.ID) {
                lock (Ghosts)
                    Ghosts.Clear();
                return;
            }

            lock (Ghosts)
                if (Ghosts.TryGetValue(move.Player.ID, out DebugMapGhost ghost))
                    Ghosts.Remove(move.Player.ID);
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            uint id = state.Player?.ID ?? uint.MaxValue;
            if (id == (Client?.PlayerInfo?.ID ?? uint.MaxValue))
                return;

            AreaKey? area = LastArea;
            if (area == null)
                return;

            lock (Ghosts)
                if (Ghosts.TryGetValue(id, out DebugMapGhost ghost) &&
                    (state.SID != area.Value.SID || state.Mode != area.Value.Mode || state.Level == CelesteNetMainComponent.LevelDebugMap))
                    Ghosts.Remove(id);
        }

        public void Handle(CelesteNetConnection con, DataPlayerFrame frame) {
            AreaKey? area = LastArea;
            if (area == null)
                return;

            lock (Ghosts) {
                if (Ghosts.TryGetValue(frame.Player.ID, out DebugMapGhost ghost) && (
                        !Client.Data.TryGetBoundRef(frame.Player, out DataPlayerState state) ||
                        state.SID != area.Value.SID ||
                        state.Mode != area.Value.Mode ||
                        state.Level == CelesteNetMainComponent.LevelDebugMap
                    )
                ) {
                    Ghosts.Remove(frame.Player.ID);
                    return;
                }

                if (ghost == null) {
                    ghost = new DebugMapGhost();
                    Ghosts[frame.Player.ID] = ghost;
                }

                ghost.Name = frame.Player.DisplayName;
                ghost.Position = frame.Position;
            }
        }

        #endregion

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (!(Engine.Scene is MapEditor)) {
                LastArea = null;
                Cleanup();
                return;
            }

            AreaKey area = (AreaKey) f_MapEditor_area.GetValue(null);
            if (LastArea == null || LastArea.Value.SID != area.SID || LastArea.Value.Mode != area.Mode) {
                LastArea = area;
                Cleanup();
            }
        }

        #region Hooks

        private void OnMapEditorRender(On.Celeste.Editor.MapEditor.orig_Render orig, MapEditor self) {
            orig(self);

            Camera camera = (Camera) f_MapEditor_Camera.GetValue(null);

            // Adapted from Everest key rendering code.

            lock (Ghosts) {
                MDraw.SpriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    camera.Matrix * Engine.ScreenMatrix
                );

                foreach (DebugMapGhost ghost in Ghosts.Values)
                    MDraw.Rect(ghost.Position.X / 8f, ghost.Position.Y / 8f - 1f, 1f, 1f, Color.HotPink);

                MDraw.SpriteBatch.End();

                MDraw.SpriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.LinearClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    null,
                    Engine.ScreenMatrix
                );

                foreach (DebugMapGhost ghost in Ghosts.Values) {
                    Vector2 pos = new Vector2(ghost.Position.X / 8f + 0.5f, ghost.Position.Y / 8f - 1.5f);
                    pos -= camera.Position;
                    pos = new Vector2((float) Math.Round(pos.X), (float) Math.Round(pos.Y));
                    pos *= camera.Zoom;
                    pos += new Vector2(960f, 540f);
                    CelesteNetClientFont.DrawOutline(
                        ghost.Name,
                        pos,
                        new Vector2(0.5f, 1f),
                        Vector2.One * 0.5f,
                        Color.White * 0.8f,
                        2f, Color.Black * 0.5f
                    );
                }

                MDraw.SpriteBatch.End();
            }
        }

        #endregion

        public class DebugMapGhost {
            public string Name;
            public Vector2 Position;
        }

    }
}
