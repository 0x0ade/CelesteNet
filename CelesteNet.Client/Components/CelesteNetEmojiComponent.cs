using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Monocle;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MDraw = Monocle.Draw;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetEmojiComponent : CelesteNetGameComponent {

        public ConcurrentDictionary<string, FileStream> Pending = new();
        public HashSet<string> Registered = new();
        public HashSet<string> RegisteredFiles = new();

        public CelesteNetEmojiComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;
        }

        public void Handle(CelesteNetConnection con, DataNetEmoji netemoji) {
            if (!Pending.TryGetValue(netemoji.ID, out FileStream cacheStream)) {
                // Create a new cache stream for the emoji
                string dir = Path.Combine(Path.GetTempPath(), "CelesteNetClientEmojiCache");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, $"{netemoji.ID}-{netemoji.GetHashCode():X8}.png");
                if (File.Exists(path))
                    File.Delete(path);

                Pending.TryAdd(netemoji.ID, cacheStream = File.OpenWrite(path));
            }

            // Add fragment data to the emoji
            cacheStream.Write(netemoji.Data, 0, netemoji.Data.Length);

            if (!netemoji.MoreFragments) {
                Pending.TryRemove(netemoji.ID, out _);
                string path = cacheStream.Name;
                cacheStream.Close();

                // Register the emoji
                RunOnMainThread(() => {
                    Logger.Log(LogLevel.VVV, "netemoji", $"Registering {netemoji.ID}");

                    bool registered = false;

                    try {
                        VirtualTexture vt = VirtualContent.CreateTexture(path);
                        MTexture mt = new(vt);
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
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            foreach (Stream s in Pending.Values)
                s.Dispose();
            Pending.Clear();

            foreach (string id in Registered)
                Emoji.Register(id, GFX.Misc["whiteCube"]);

            Emoji.Fill(CelesteNetClientFont.Font);

            foreach (string path in RegisteredFiles)
                if (File.Exists(path))
                    File.Delete(path);
        }

    }
}
