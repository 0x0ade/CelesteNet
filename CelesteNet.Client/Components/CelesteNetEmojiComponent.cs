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
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetEmojiComponent : CelesteNetGameComponent {

        public HashSet<string> Registered = new HashSet<string>();
        public HashSet<string> RegisteredFiles = new HashSet<string>();

        public CelesteNetEmojiComponent(CelesteNetClientComponent context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;
        }

        public void Handle(CelesteNetConnection con, DataNetEmoji netemoji) {
            Logger.Log(LogLevel.VVV, "netemoji", $"Received {netemoji.ID}");

            string dir = Path.Combine(Path.GetTempPath(), "CelesteNetClientEmojiCache");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string path = Path.Combine(dir, $"{netemoji.ID}-{netemoji.GetHashCode():X8}.png");
            using (FileStream fs = File.OpenWrite(path))
            using (MemoryStream ms = new MemoryStream(netemoji.Data))
                ms.CopyTo(fs);

            RunOnMainThread(() => {
                Logger.Log(LogLevel.VVV, "netemoji", $"Registering {netemoji.ID}");
                // FIXME: UNREGISTER EMOJI!!!!

                bool registered = false;

                try {
                    VirtualTexture vt = VirtualContent.CreateTexture(path);
                    MTexture mt = new MTexture(vt);
                    if (vt.Texture_Safe == null) // Needed to trigger lazy loading.
                        throw new Exception($"Couldn't load emoji {netemoji.ID}");

                    Registered.Add(netemoji.ID);
                    RegisteredFiles.Add(path);
                    Emoji.Register(netemoji.ID, mt);
                    Emoji.Fill(CelesteNetClientFont.Font);
                    registered = true;

                } finally {
                    if (!registered)
                        File.Delete(path);
                }
            });
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            // FIXME: UNREGISTER EMOJI!!!!
            foreach (string path in RegisteredFiles)
                if (File.Exists(path))
                    File.Delete(path);
        }

    }
}
