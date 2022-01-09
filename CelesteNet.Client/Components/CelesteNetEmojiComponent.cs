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

        public override void Init() {
            base.Init();

            RunOnMainThread(() => {
                RegisterEmote("strawberry", GFX.Gui, "collectables/strawberry",      fillFont: false);
                RegisterEmote("heart",      GFX.Gui, "collectables/heartgem/0/spin", fillFont: false);
                RegisterEmote("feather",      GFX.Gui, "feather/feather", fillFont: false);

                // chapter icons
                RegisterEmote("prologue",         GFX.Gui, "areas/intro",      fillFont: false);
                RegisterEmote("forsaken_city",    GFX.Gui, "areas/city",       fillFont: false);
                RegisterEmote("old_site",         GFX.Gui, "areas/oldsite",    fillFont: false);
                RegisterEmote("celestial_resort", GFX.Gui, "areas/resort",     fillFont: false);
                RegisterEmote("golden_ridge",     GFX.Gui, "areas/cliffside",  fillFont: false);
                RegisterEmote("mirror_temple",    GFX.Gui, "areas/temple",     fillFont: false);
                RegisterEmote("reflection",       GFX.Gui, "areas/reflection", fillFont: false);
                RegisterEmote("the_summit",       GFX.Gui, "areas/Summit",     fillFont: false);
                RegisterEmote("epilogue",         GFX.Gui, "areas/intro",      fillFont: false);
                RegisterEmote("core",             GFX.Gui, "areas/core",       fillFont: false);
                RegisterEmote("farewell",         GFX.Gui, "areas/farewell",   fillFont: false);

                Emoji.Fill(CelesteNetClientFont.Font);
            });
        }

        public void RegisterEmote(string name, Atlas atlas, string path, int index = 0, int w = 64, int h = 64, bool fillFont = true) {
            MTexture icon = atlas.GetAtlasSubtexturesAt(path, index);

            if (icon != null && icon.Texture?.Texture_Safe != null) {
                icon = new(icon.Parent, icon.ClipRect);

                Emoji.Register(name, icon, w, h);
            }

            if (!Emoji.TryGet(name, out char _)) {
                Logger.Log(LogLevel.VVV, "netemoji", $"Could not register {name} emoji.");
                return;
            }

            Registered.Add(name);

            if (fillFont)
                Emoji.Fill(CelesteNetClientFont.Font);

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

                    } catch (InvalidOperationException e) {
                        Logger.Log(LogLevel.WRN, "netemoji", $"Registering {netemoji.ID} failed due to {e.GetType().Name}: {e.Message}");
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
