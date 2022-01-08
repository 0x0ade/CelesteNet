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

        public class NetEmojiContent : ModContent {

            protected override void Dispose(bool disposing) {
                foreach (ModAsset asset in this.List) {
                    if (asset is NetEmojiAsset netemoji)
                        netemoji.Dispose();
                }
                base.Dispose(disposing);
            }

            public HashSet<string> Registered = new();
            protected override void Crawl() {}

        }

        public class NetEmojiAsset : ModAsset, IDisposable {

            private int nextSeq = 0;
            private MemoryStream memStream = new();

            public new NetEmojiContent Source => (NetEmojiContent) base.Source;
            public readonly string ID;
            public bool Pending => nextSeq >= 0;

            public NetEmojiAsset(NetEmojiContent content, string id) : base(content) {
                ID = id;
            }

            public void Dispose() {
                memStream.Dispose();
                if (!Pending) {
                    Emoji.Register(ID, GFX.Misc["whiteCube"]);
                    Emoji.Fill(CelesteNetClientFont.Font);
                }
            }

            public void HandleFragment(DataNetEmoji data) {
                // The emoji must be pending
                if (!Pending)
                    throw new InvalidOperationException($"NetEmoji '{ID}' isn't pending!");

                // Check and increase the sequence number
                if (data.SequenceNumber != nextSeq)
                    throw new InvalidOperationException($"Unexpected NetEmoji '{ID}' sequence number: expected {nextSeq}, got {data.SequenceNumber}!");
                nextSeq = (nextSeq + 1) % DataNetEmoji.MaxSequenceNumber;

                // Add fragment data to the emoji
                memStream.Write(data.Data, 0, data.Data.Length);

                // Check if there are more fragments
                if (!data.MoreFragments) {
                    nextSeq = -1;

                    // Register the emoji
                    MTexture tex = new MTexture(VirtualContent.CreateTexture(this)); 
                    Source.Registered.Add(ID);
                    Emoji.Register(ID, tex);
                    Emoji.Fill(CelesteNetClientFont.Font);
                }
            }

            protected override void Open(out Stream stream, out bool isSection) {
                // The emoji mustn't be pending
                if (Pending)
                    throw new InvalidOperationException($"NetEmoji '{ID}' is pending!");

                // Don't duplicate the memory
                stream = new MemoryStream(memStream.GetBuffer());
                isSection = false;
            }

        }

        public NetEmojiContent Content = new();
        private Dictionary<string, NetEmojiAsset> Pending = new();

        public CelesteNetEmojiComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            Visible = false;
        }

        public void Handle(CelesteNetConnection con, DataNetEmoji netemoji) {
            lock (Pending) {
                // Get the emoji asset
                if (!Pending.TryGetValue(netemoji.ID, out NetEmojiAsset asset))
                    Pending.Add(netemoji.ID, asset = new(Content, netemoji.ID));

                // Handle the fragment
                asset.HandleFragment(netemoji);

                // If the emoji isn't pending anymore, remove it from the set
                if (!asset.Pending)
                    Pending.Remove(netemoji.ID);
            }
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            Content.Dispose();
        }

    }
}
