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

        protected readonly string[] DefaultEmoji = 
        { 
            "strawberry=i:collectables/strawberry", 
            "heart=i:collectables/heartgem/0/spin", 
            "feather=i:feather/feather", 
            "forsaken_city=i:areas/city",
            "old_site=i:areas/oldsite",
            "celestial_resort=i:areas/resort",
            "golden_ridge=i:areas/cliffside",
            "mirror_temple=i:areas/temple",
            "reflection=i:areas/reflection", 
            "the_summit=i:areas/Summit",
            "core=i:areas/core",
            "farewell=i:areas/farewell",
            "intro=i:areas/intro"
        };
        protected List<MTexture> DefaultEmojiIcons = new();

        public CelesteNetEmojiComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;
        }

        public override void Init() {
            base.Init();

            RunOnMainThread(() => {
                foreach (string emoji in DefaultEmoji) {

                    string[] emoji_parts = emoji.Split('=');

                    string emoji_name = emoji_parts.Length > 0 ? emoji_parts[0] : emoji;
                    string emoji_icon = emoji_parts.Length > 1 ? emoji_parts[1] : emoji;

                    Logger.Log(LogLevel.VVV, "netemoji", $"Registering {emoji_icon} as {emoji_name} at size 64");

                    MTexture icon = GhostEmote.GetIcon(emoji_icon, 0.0f);

                    if (icon != null && icon.Texture?.Texture_Safe != null) {
                        icon = new(icon.Parent, icon.ClipRect);

                        DefaultEmojiIcons.Add(icon);

                        Emoji.Register(emoji_name, icon, 64);
                    }

                    if (!Emoji.TryGet(emoji_name, out char _)) {
                        Logger.Log(LogLevel.VVV, "netemoji", $"Could not get {emoji_name} emoji.");
                    }
                }
                Emoji.Fill(CelesteNetClientFont.Font);
            });
        }

        public void Handle(CelesteNetConnection con, DataNetEmoji netemoji) {
            FileStream cacheStream;
            if (netemoji.FirstFragment) {
                // Create a new cache stream for the emoji
                string dir = Path.Combine(Path.GetTempPath(), "CelesteNetClientEmojiCache");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, $"{netemoji.ID}-{netemoji.GetHashCode():X8}.png");
                if (File.Exists(path))
                    File.Delete(path);

                Pending.TryAdd(netemoji.ID, cacheStream = File.OpenWrite(path));
            } else if (!Pending.TryGetValue(netemoji.ID, out cacheStream)) {
                throw new InvalidDataException($"Missing first fragment of emoji '{netemoji.ID}'!");
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
