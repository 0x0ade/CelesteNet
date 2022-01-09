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
    public class CelesteNetPlayerListComponent : CelesteNetGameComponent {

        public float Scale => Settings.UIScale;

        public readonly Color ColorCountHeader = Calc.HexToColor("FFFF77");
        public readonly Color ColorChannelHeader = Calc.HexToColor("DDDD88");
        public readonly Color ColorChannelHeaderOwn = Calc.HexToColor("FFFF77");
        public readonly Color ColorChannelHeaderPrivate = Calc.HexToColor("DDDD88") * 0.6f;
        public static readonly Color DefaultLevelColor = Color.LightGray;

        public bool Active;

        private List<Blob> List = new();

        public DataChannelList Channels;

        public ListMode Mode => Settings.PlayerListMode;
        private ListMode LastMode;

        public enum ListMode {
            Channels,
            Classic,
            CompactChannels
        }

        public CelesteNetPlayerListComponent(CelesteNetClientContext context, Game game)
            : base(context, game) {

            UpdateOrder = 10000;
            DrawOrder = 10001;
        }

        public void RebuildList() {
            if (MDraw.DefaultFont == null || Client == null || Channels == null)
                return;

            DataPlayerInfo[] all = Client.Data.GetRefs<DataPlayerInfo>();

            List<Blob> list = new() {
                new Blob {
                    Name = $"{all.Length} player{(all.Length == 1 ? "" : "s")}",
                    Color = ColorCountHeader
                }
            };


            switch (Mode) {
                case ListMode.Classic:
                    foreach (DataPlayerInfo player in all.OrderBy(p => GetOrderKey(p))) {
                        if (string.IsNullOrWhiteSpace(player.DisplayName))
                            continue;

                        BlobPlayer blob = new();

                        // TODO: Figure out proper way to get Avatar by itself?
                        // Player only has Name/FullName/DisplayName... with Avatar in the latter :(
                        blob.Name = player.DisplayName;

                        DataChannelList.Channel channel = Channels.List.FirstOrDefault(c => c.Players.Contains(player.ID));
                        if (channel != null && !string.IsNullOrEmpty(channel.Name)) {
                            blob.Name += $" #{channel.Name}";
                        }

                        if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                            GetState(ref blob, state);

                        list.Add(blob);
                    }

                    break;

                case ListMode.Channels:
                case ListMode.CompactChannels:
                    HashSet<DataPlayerInfo> listed = new();

                    DataChannelList.Channel own = Channels.List.FirstOrDefault(c => c.Players.Contains(Client.PlayerInfo.ID));

                    if (own != null) {
                        list.Add(new() {
                            Name = own.Name,
                            Color = ColorChannelHeaderOwn
                        });

                        foreach (DataPlayerInfo player in own.Players.Select(p => GetPlayerInfo(p)).OrderBy(p => GetOrderKey(p))) {
                            BlobPlayer blob = new() { ScaleFactor = 0.5f };
                            listed.Add(ListPlayerUnderChannel(ref blob, player));

                            if (Mode == ListMode.CompactChannels) {
                                blob.Location.Name = "";
                                blob.Location.Side = "";
                                blob.Location.Level = "";
                            }
                            list.Add(blob);
                        }
                    }

                    foreach (DataChannelList.Channel channel in Channels.List) {
                        if (channel == own)
                            continue;

                        list.Add(new() {
                            Name = channel.Name,
                            Color = ColorChannelHeader
                        });

                        foreach (DataPlayerInfo player in channel.Players.Select(p => GetPlayerInfo(p)).OrderBy(p => GetOrderKey(p))) {
                            BlobPlayer blob = new() { ScaleFactor = 1f };
                            listed.Add(ListPlayerUnderChannel(ref blob, player));

                            if (Mode == ListMode.CompactChannels) {
                                blob.Location.Name = "";
                                blob.Location.Side = "";
                                blob.Location.Level = "";
                            }
                            list.Add(blob);
                        }
                    }

                    bool wrotePrivate = false;

                    foreach (DataPlayerInfo player in all.OrderBy(p => GetOrderKey(p))) {
                        if (listed.Contains(player) || string.IsNullOrWhiteSpace(player.DisplayName))
                            continue;

                        if (!wrotePrivate) {
                            wrotePrivate = true;
                            list.Add(new() {
                                Name = "!<private>",
                                Color = ColorChannelHeaderPrivate
                            });
                        }

                        list.Add(new() {
                            Name = player.DisplayName,
                            ScaleFactor = 1f
                        });
                    }

                    break;
            }

            List = list;
        }

        private string GetOrderKey(DataPlayerInfo player) {
            if (player == null)
                return "9";

            if (Client.Data.TryGetBoundRef(player, out DataPlayerState state) && !string.IsNullOrEmpty(state?.SID))
                return $"0 {(state.SID != null ? "0" + state.SID : "9")} {player.FullName}";

            return $"8 {player.FullName}";
        }

        private DataPlayerInfo GetPlayerInfo(uint id) {
            if (Client.Data.TryGetRef(id, out DataPlayerInfo player) && !string.IsNullOrEmpty(player?.DisplayName))
                return player;
            return null;
        }

        private DataPlayerInfo ListPlayerUnderChannel(ref BlobPlayer blob, DataPlayerInfo player) {
            if (player != null) {
                blob.Name = player.DisplayName;

                if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                    GetState(ref blob, state);

                return player;

            } else {
                blob.Name = "?";
                return null;
            }
        }

        private void GetState(ref BlobPlayer blob, DataPlayerState state) {

            if (!string.IsNullOrWhiteSpace(state.SID)) {
                AreaData area = AreaDataExt.Get(state.SID);
                string chapter = area?.Name?.DialogCleanOrNull(Dialog.Languages["english"]) ?? state.SID;

                if (area?.LevelSet == "Celeste") {
                    blob.Location.Color = DefaultLevelColor;
                    blob.Location.TitleColor = Color.Lerp(area?.CassseteNoteColor ?? Color.White, DefaultLevelColor, 0.5f);
                    blob.Location.AccentColor = Color.Lerp(area?.TitleAccentColor ?? Color.White, DefaultLevelColor, 0.5f);
                } else {
                    blob.Location.Color = DefaultLevelColor;
                    blob.Location.TitleColor = Color.Lerp(area?.TitleAccentColor ?? Color.White, DefaultLevelColor, 0.5f);
                    blob.Location.AccentColor = Color.Lerp(area?.TitleBaseColor ?? Color.White, DefaultLevelColor, 0.5f);
                }

                blob.Location.Icon = area?.Icon ?? "";

                string[] areaPath = state.SID.Split('/');
                if (areaPath.Length >= 3) {
                    AreaData lobby = AreaDataExt.Get(areaPath[0] + "/0-Lobbies/" + areaPath[1]);
                    if (lobby?.Icon != null)
                        blob.Location.Icon = lobby.Icon;
                }

                blob.Location.Name = chapter;
                blob.Location.Side = ((char)('A' + (int) state.Mode)).ToString();
                blob.Location.Level = state.Level;

                ShortenRandomizerLocation(ref blob.Location);
            }

            blob.Idle = state.Idle;
        }

        private void ShortenRandomizerLocation(ref BlobLocation location) {
            /*
             * Randomizer Locations usually are very long like
             * Celeste/1-ForsakenCity/A/b-02/31 randomizer/Mirror Temple_0_1234567 A
             */

            if (!location.Name.StartsWith("randomizer/") || !Settings.PlayerListShortenRandomizer)
                return;

            // shorten the randomizer/ part down
            location.Name = "rnd/" + location.Name.Substring("randomizer/".Length);

            // yoink out all the funny numbers like _0_1234567 at the end
            location.Name = location.Name.TrimEnd("_0123456789".ToCharArray());

            if (location.Level.StartsWith("Celeste/"))
                location.Level = location.Level.Substring("Celeste/".Length);

            location.Level = location.Level.Replace("SpringCollab2020", "sc2020");
            location.Level = location.Level.Replace("0-Gyms", "Gym");
            location.Level = location.Level.Replace("0-Prologue", "Prolg");
            location.Level = location.Level.Replace("1-Beginner", "Beg");
            location.Level = location.Level.Replace("2-Intermediate", "Int");
            location.Level = location.Level.Replace("3-Advanced", "Adv");
            location.Level = location.Level.Replace("4-Expert", "Exp");
            location.Level = location.Level.Replace("5-Grandmaster", "GM");

            // Side seems to always be 0 = A so clear that
            location.Side = "";
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo info) {
            RebuildList();
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            RebuildList();
        }

        public void Handle(CelesteNetConnection con, DataChannelList channels) {
            Channels = channels;
            RebuildList();
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (LastMode != Mode) {
                LastMode = Mode;
                RebuildList();
            }

            if (!(Engine.Scene?.Paused ?? false) && Settings.ButtonPlayerList.Button.Pressed)
                Active = !Active;
        }

        public override void Draw(GameTime gameTime) {
            if (Active)
                base.Draw(gameTime);
        }

        protected override void Render(GameTime gameTime, bool toBuffer) {
            float scale = Scale;

            float y = 50f * scale;

            SpeedrunTimerDisplay timer = Engine.Scene?.Entities.FindFirst<SpeedrunTimerDisplay>();
            if (timer != null) {
                switch (global::Celeste.Settings.Instance?.SpeedrunClock ?? SpeedrunType.Off) {
                    case SpeedrunType.Off:
                        break;

                    case SpeedrunType.Chapter:
                        if (timer.CompleteTimer < 3f)
                            y += 104f;
                        break;

                    case SpeedrunType.File:
                    default:
                        y += 104f + 24f;
                        break;
                }
            }

            List<Blob> list = List;

            int textScaleTry = 0;
            float textScale = scale;
            RetryLineScale:

            Vector2 sizeAll = Vector2.Zero;

            foreach (Blob blob in list) {
                blob.DynScale = Calc.LerpClamp(scale, textScale, blob.ScaleFactor);
                blob.DynY = sizeAll.Y;
                Vector2 size = CelesteNetClientFont.Measure((blob as BlobPlayer)?.Text ?? blob.Text) * blob.DynScale;
                if (blob is BlobPlayer p) {
                    // insert extra space for the idle icon on non-idle players too.
                    if(!p.Idle)
                       size.X += CelesteNetClientFont.Measure(BlobPlayer.IdleIconCode + " ").X * blob.DynScale;
                    // Adjust for Randomizer locations getting shrunk
                    size.X += (CelesteNetClientFont.Measure(p.Location.Text + " ").X + (GFX.Gui.Has(p.Location.Icon) ? 64f : 0f)) * blob.DynScale * p.Location.DynScale;
                }
                sizeAll.X = Math.Max(sizeAll.X, size.X);
                sizeAll.Y += size.Y + 10f * scale;

                if (((sizeAll.X + 100f * scale) > UI_WIDTH * 0.7f || (sizeAll.Y + 90f * scale) > UI_HEIGHT * 0.7f) && textScaleTry < 5) {
                    textScaleTry++;
                    textScale -= scale * 0.1f;
                    goto RetryLineScale;
                }
            }

            Context.RenderHelper.Rect(25f * scale, y - 25f * scale, sizeAll.X + 50f * scale, sizeAll.Y + 40f * scale, Color.Black * 0.8f);

            foreach (Blob blob in list) {
                Vector2 blobDynScale = new(blob.DynScale);

                if (blob is BlobPlayer player) {
                    string playerinfo = string.Join(" ",
                        new List<string>() {
                        player.Icon,
                        player.Name,
                        player.IdleIcon
                        }.Where(item => !string.IsNullOrEmpty(item))
                    );

                    CelesteNetClientFont.Draw(
                        playerinfo,
                        new(50f * scale, y + blob.DynY),
                        Vector2.Zero,
                        blobDynScale,
                        player.Color
                    );

                    if (!string.IsNullOrEmpty(player.Location.Name) || !string.IsNullOrEmpty(player.Location.Icon)) {
                        blobDynScale *= new Vector2(player.Location.DynScale);

                        // Organizing the different parts with their respective colors
                        List<Tuple<string, Color>> location = new() 
                        {
                            new(player.Location.Level, player.Location.Color),
                            new(":",        Color.Lerp(player.Location.Color, Color.Black, 0.5f)),
                            new(player.Location.Name,  player.Location.TitleColor),
                            new(player.Location.Side,  player.Location.AccentColor)
                            //new(player.Location.Icon,  player.Location.Color)
                        };
                        // Rendering Location bits right-to-left, hence the Reverse and the justify = Vector2.UnitX
                        location.Reverse();

                        float x = sizeAll.X - 64f * blobDynScale.X;
                        foreach (Tuple<string, Color> t in location) {
                            CelesteNetClientFont.Draw(
                                t.Item1,
                                new(50f * scale + x, y + player.DynY + 5f * (1f - player.Location.DynScale)),
                                Vector2.UnitX,
                                blobDynScale,
                                t.Item2
                            );
                            x -= CelesteNetClientFont.Measure(t.Item1 + " ").X * blobDynScale.X;
                        }

                        MTexture icon = GFX.Gui.Has(player.Location.Icon) ? GFX.Gui.GetAtlasSubtexturesAt(player.Location.Icon, 0) : null;

                        if (icon != null) {
                            icon.Draw(
                                new(50f * scale + sizeAll.X - 64f * blobDynScale.X, y + player.DynY),
                                Vector2.Zero,
                                Color.White,
                                Math.Min(64f / icon.ClipRect.Width, 64f / icon.ClipRect.Height) * blobDynScale
                            );
                        }
                    }
                } else {
                    string blobinfo = string.Join(" ",
                        new List<string>() {
                                            blob.Icon,
                                            blob.Name
                        }.Where(item => !string.IsNullOrEmpty(item)));

                    CelesteNetClientFont.Draw(
                        blobinfo,
                        new(50f * scale, y + blob.DynY),
                        Vector2.Zero,
                        new(blob.DynScale, blob.DynScale),
                        blob.Color
                    );
                }
            }

        }

        public class Blob {
            public string Text {
                get {
                    return string.Join(" ", 
                        new List<string>() { 
                            Icon, 
                            Name
                        }.Where(item => !string.IsNullOrEmpty(item)));
                }
            }

            public string Name = "";
            public string Icon = "";
            public Color Color = Color.White;
            public float ScaleFactor = 0f;
            public float DynY;
            public float DynScale;
        }

        public class BlobPlayer : Blob {
            public new string Text {
                get {
                    return string.Join(" ",
                        new List<string>() {
                            Icon,
                            Name,
                            IdleIcon
                        }.Where(item => !string.IsNullOrEmpty(item)));
                }
            }
            public BlobLocation Location = new();
            public static readonly string IdleIconCode = ":celestenet_idle:";
            public string IdleIcon => Idle ? IdleIconCode : "";
            public bool Idle;

        }

        public class BlobLocation : Blob {
            public new string Text {
                get {
                    return string.Join(" ",
                        new List<string>() {
                            Name,
                            Side,
                            ":",
                            Level
                            //Icon
                        }.Where(item => !string.IsNullOrEmpty(item)));
                }
            }
            public bool IsRandomizer => Name.StartsWith("rnd/") || Name.StartsWith("randomizer/");
            public new float DynScale => IsRandomizer ? 0.9f : 1f;
            public string Side = "";
            public string Level = "";
            public new Color Color = DefaultLevelColor;
            public Color TitleColor = DefaultLevelColor;
            public Color AccentColor = DefaultLevelColor;
        }

    }
}
