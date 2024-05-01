using System;
using System.Collections.Generic;
using System.IO;
using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.CelesteNet.Client.Components {
    public class CelesteNetEmojiComponent : CelesteNetGameComponent {

        public const string AvatarMissing = ":celestenet_avatar_missing:";

        public class NetEmojiContent : ModContent {

            public HashSet<string> Registered = new();

            protected override void Dispose(bool disposing) {
                foreach (ModAsset asset in List) {
                    if (asset is NetEmojiAsset netemoji)
                        netemoji.Dispose();
                }
                base.Dispose(disposing);
            }

            protected override void Crawl() {}

        }

        public class NetEmojiAsset : ModAsset, IDisposable {

            private int NextSeq = 0;
            private readonly MemoryStream Buffer = new();

            public new NetEmojiContent Source => (NetEmojiContent) base.Source;
            public readonly string ID;
            public bool Pending => NextSeq >= 0;

            public NetEmojiAsset(NetEmojiContent content, string id) : base(content) {
                ID = id;
                Type = typeof(Texture2D);
                Format = "png";
                PathVirtual = $"emoji/{ID}";
                MainThreadHelper.Schedule(() => {
                    Emoji.Register(ID, GFX.Misc["whiteCube"]);
                    Emoji.Fill(CelesteNetClientFont.Font);
                });
            }

            public void Dispose() {
                Buffer.Dispose();
                if (!Pending) {
                    MainThreadHelper.Schedule(() => {
                        Emoji.Register(ID, GFX.Misc["whiteCube"]);
                        Emoji.Fill(CelesteNetClientFont.Font);
                    });
                }
            }

            public void HandleFragment(DataNetEmoji data) {
                // The emoji must be pending
                if (!Pending)
                    throw new InvalidOperationException($"NetEmoji '{ID}' isn't pending!");

                // Check and increase the sequence number
                if (data.SequenceNumber != NextSeq)
                    throw new InvalidOperationException($"Unexpected NetEmoji '{ID}' sequence number: expected {NextSeq}, got {data.SequenceNumber}!");
                NextSeq = (NextSeq + 1) % DataNetEmoji.MaxSequenceNumber;

                // Add fragment data to the emoji
                Buffer.Write(data.Data, 0, data.Data.Length);

                // Check if there are more fragments
                if (!data.MoreFragments)
                    NextSeq = -1;
            }

            protected override void Open(out Stream stream, out bool isSection) {
                // The emoji mustn't be pending
                if (Pending)
                    throw new InvalidOperationException($"NetEmoji '{ID}' is pending!");

                // Don't duplicate the memory
                stream = new MemoryStream(Buffer.GetBuffer());
                isSection = false;
            }

        }

        public NetEmojiContent Content = new();
        private readonly Dictionary<string, NetEmojiAsset> Pending = new();

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

                // Check if the emoji isn't pending anymore
                if (!asset.Pending) {
                    Pending.Remove(netemoji.ID);

                    // Register the emoji
                    try {
                        MainThreadHelper.Schedule(() => {
                            VirtualTexture vtex;
                            try {
                                vtex = VirtualContent.CreateTexture(asset);
                            } catch (Exception e) {
                                Logger.Log(LogLevel.ERR, "emoji", $"Failed to load emoji: {netemoji.ID} - {e}");
                                return;
                            }
                            MTexture tex = new(vtex);
                            Content.Registered.Add(asset.ID);
                            Emoji.Register(asset.ID, tex);
                            Emoji.Fill(CelesteNetClientFont.Font);

                            // find the corresponding DataPlayerInfo (perhaps DataNetEmoji should hold a Ref)
                            string[] splitID = netemoji.ID.Split('_');
                            if (splitID.Length > 2 && uint.TryParse(splitID[2], out uint ID)
                            && Client?.Data != null && Client.Data.TryGetRef(ID, out DataPlayerInfo player) && player != null) {
                                // restore the avatar in case it has been set to AvatarMissing by the Filter below
                                player.DisplayName = $":{netemoji.ID}: {player.FullName}";
                            }
                        });
                    } catch (ObjectDisposedException) {
                        // Main thread died and queue closed, whoops.
                    }
                }
            }
        }

        public bool Filter(CelesteNetConnection con, DataPlayerInfo info) {
            if (info == null || Client?.Options == null || Client.Options.AvatarsDisabled)
                return true;

            // catch missing avatars - "restoring" these happens in Handle above when the avatar is fully received
            string avatar = $"celestenet_avatar_{info.ID}_";
            if (!Emoji.Registered.Contains(avatar)) {
                info.DisplayName = info.DisplayName.Replace($":{avatar}:", AvatarMissing);
            }
            return true;
        }

        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);

            Content.Dispose();
        }

    }
}
