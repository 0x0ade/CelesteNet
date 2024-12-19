using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Celeste.Editor;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetDebugMapComponent : CelesteNetGameComponent {

        private static readonly FieldInfo? f_MapEditor_area =
            typeof(MapEditor)
            .GetField("area", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo? f_MapEditor_Camera =
            typeof(MapEditor)
            .GetField("Camera", BindingFlags.NonPublic | BindingFlags.Static);

        private AreaKey? LastArea;

        public Dictionary<uint, DebugMapGhost> Ghosts = new();

        public CelesteNetDebugMapComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10300;
            Visible = false;
        }

        public override void Initialize() {
            base.Initialize();

            MainThreadHelper.Schedule(() => {
                On.Celeste.Editor.MapEditor.ctor += OnMapEditorCtor;
                On.Celeste.Editor.MapEditor.Render += OnMapEditorRender;
            });
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            try {
                MainThreadHelper.Schedule(() => {
                    On.Celeste.Editor.MapEditor.ctor -= OnMapEditorCtor;
                    On.Celeste.Editor.MapEditor.Render -= OnMapEditorRender;
                });
            } catch (ObjectDisposedException) {
                // It might already be too late to tell the main thread to do anything.
            }

            Cleanup();
        }

        public void Cleanup() {
            lock (Ghosts)
                if (Ghosts.Count > 0)
                    Ghosts.Clear();
        }

        #region Handlers

        public void Handle(CelesteNetConnection con, DataPlayerInfo player) {
            if (Client?.PlayerInfo == null || player.ID == Client.PlayerInfo.ID || LastArea == null)
                return;

            lock (Ghosts)
                if (Ghosts.TryGetValue(player.ID, out DebugMapGhost? ghost) &&
                    string.IsNullOrEmpty(player.DisplayName))
                    Ghosts.Remove(player.ID);
        }

        public void Handle(CelesteNetConnection con, DataChannelMove move) {
            if (LastArea == null || Client?.PlayerInfo == null || move.Player == null)
                return;

            if (move.Player.ID == Client.PlayerInfo.ID) {
                lock (Ghosts)
                    Ghosts.Clear();
                return;
            }

            lock (Ghosts)
                if (Ghosts.TryGetValue(move.Player.ID, out DebugMapGhost? ghost))
                    Ghosts.Remove(move.Player.ID);
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            uint id = state.Player?.ID ?? uint.MaxValue;
            if (id == (Client?.PlayerInfo?.ID ?? uint.MaxValue))
                return;

            lock (Ghosts) {
                if (LastArea is not AreaKey area)
                    return;

                if (Ghosts.TryGetValue(id, out DebugMapGhost? ghost) &&
                    (state.SID != area.SID || state.Mode != area.Mode || state.Level == CelesteNetMainComponent.LevelDebugMap))
                    Ghosts.Remove(id);
            }
        }

        public void Handle(CelesteNetConnection con, DataPlayerFrame frame) {
            if (LastArea is not AreaKey area || Client?.Data == null || frame.Player == null)
                return;

            DebugMapGhost? ghost = null;

            lock (Ghosts) {
                if (!Client.Data.TryGetBoundRef(frame.Player, out DataPlayerState? state) || 
                    (state != null &&
                    Ghosts.TryGetValue(frame.Player.ID, out ghost) && (
                        state.SID != area.SID ||
                        state.Mode != area.Mode ||
                        state.Level == CelesteNetMainComponent.LevelDebugMap
                    ))
                ) {
                    Ghosts.Remove(frame.Player.ID);
                    return;
                }

                if (ghost == null) {
                    ghost = new();
                    Ghosts[frame.Player.ID] = ghost;
                }

                ghost.Name = frame.Player.DisplayName;
                ghost.Position = frame.Position;
                if (state != null) {
                    ghost.SID = state.SID;
                    ghost.Mode = state.Mode;
                }
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

            VerifyArea();
        }

        private void VerifyArea() {
            AreaKey? area = (AreaKey?) f_MapEditor_area?.GetValue(null);
            if (LastArea == null || area == null || LastArea.Value.ID != area.Value.ID || LastArea.Value.Mode != area.Value.Mode) {
                lock (Ghosts) {
                    LastArea = area;
                    Cleanup();
                    if (Client?.Con != null && Context?.Main != null)
                        foreach (DataPlayerFrame frame in Context.Main.LastFrames.Values.ToArray())
                            Handle(Client.Con, frame);
                }
            }
        }

        #region Hooks

        private void OnMapEditorCtor(On.Celeste.Editor.MapEditor.orig_ctor orig, MapEditor self, AreaKey area, bool reloadMapData) {
            orig(self, area, reloadMapData);
            VerifyArea();
        }

        private void OnMapEditorRender(On.Celeste.Editor.MapEditor.orig_Render orig, MapEditor self) {
            orig(self);

            string sid = "";
            AreaMode mode = (AreaMode)(-1);

            if (LastArea != null) {
                sid = LastArea.Value.SID;
                mode = LastArea.Value.Mode;
            }

            Camera? camera = (Camera?) f_MapEditor_Camera?.GetValue(null);

            if (camera == null)
                return;

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
                    if (ghost.SID == sid && ghost.Mode == mode && ghost.Position != null)
                        MDraw.Rect(ghost.Position.Value.X / 8f, ghost.Position.Value.Y / 8f - 1f, 1f, 1f, Color.HotPink);

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
                    if (ghost.SID != sid || ghost.Mode != mode || ghost.Position == null)
                        continue;
                    Vector2 pos = new(ghost.Position.Value.X / 8f + 0.5f, ghost.Position.Value.Y / 8f - 1.5f);
                    pos -= camera.Position;
                    pos = new((float) Math.Round(pos.X), (float) Math.Round(pos.Y));
                    pos *= camera.Zoom;
                    pos += new Vector2(960f, 540f);
                    CelesteNetClientFont.DrawOutline(
                        ghost.Name,
                        pos,
                        new(0.5f, 1f),
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
            public string Name = "";
            public Vector2? Position;
            public string? SID;
            public AreaMode? Mode;
        }

    }
}
