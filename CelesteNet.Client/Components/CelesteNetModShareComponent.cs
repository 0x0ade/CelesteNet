using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetModShareComponent : CelesteNetGameComponent {

        public List<EverestModuleMetadata> Requested = new List<EverestModuleMetadata>();

        public CelesteNetModShareComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;
        }

        public override void Initialize() {
            base.Initialize();

            IL.Celeste.Mod.UI.OuiModOptions.CreateMenu += ILOuiModOptionsCreateMenu;
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            MainThreadHelper.Do(() => {
                IL.Celeste.Mod.UI.OuiModOptions.CreateMenu -= ILOuiModOptionsCreateMenu;
            });
        }

        public void Handle(CelesteNetConnection con, DataMapModInfoRequest request) {
            string sid = request.MapSID;
            if (string.IsNullOrEmpty(sid))
                sid = (Engine.Scene as Level)?.Session?.Area.SID;

            if (string.IsNullOrEmpty(sid))
                goto Error;

            AreaData area = AreaData.Get(sid);
            if (area == null)
                goto Error;

            if (!Everest.Content.TryGet<AssetTypeMap>($"Maps/{area.SID}", out ModAsset asset))
                goto Error;

            EverestModuleMetadata mod = asset?.Source?.Mod;
            if (mod == null)
                goto Error;

            Client?.Send(new DataMapModInfo {
                RequestID = request.ID,

                MapSID = sid,
                MapName = Dialog.Clean(area.Name),
                ModID = mod.Name,
                ModName = string.IsNullOrEmpty(mod.Name) ? "" : Dialog.Clean($"modname_{mod.Name.DialogKeyify()}"),
                ModVersion = mod.Version
            });

            return;

            Error:
            Client?.Send(new DataMapModInfo {
                RequestID = request.ID
            });
            return;
        }

        public void Handle(CelesteNetConnection con, DataModRec rec) {
            Logger.Log(LogLevel.CRI, "netmod", $"Server recommended mod: {rec.ModName} ({rec.ModID} v{rec.ModVersion})");

            Context.Status.Set($"Check mod options to install {rec.ModName}");

            lock (Requested) {
                int index = Requested.FindIndex(other => other.Name == rec.ModID);
                if (index != -1) {
                    Requested[index].Version = rec.ModVersion;
                } else {
                    Requested.Add(new EverestModuleMetadata {
                        Name = rec.ModName,
                        Version = rec.ModVersion
                    });
                }
            }
        }

        #region Hooks

        private void ILOuiModOptionsCreateMenu(ILContext il) {
            ILCursor c = new ILCursor(il);
            c.GotoNext(i => i.MatchNewobj<List<EverestModuleMetadata>>());
            c.Index++;
            c.EmitDelegate<Func<List<EverestModuleMetadata>, List<EverestModuleMetadata>>>(list => {
                lock (Requested)
                    return list.Concat(Requested).ToList();
            });
        }

        #endregion

    }
}
