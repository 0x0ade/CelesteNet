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

        public static event Action<BlobPlayer, DataPlayerState> OnGetState;

        public float Scale => Settings.UIScale;

        public readonly Color ColorCountHeader = Calc.HexToColor("FFFF77");
        public readonly Color ColorChannelHeader = Calc.HexToColor("DDDD88");
        public readonly Color ColorChannelHeaderOwn = Calc.HexToColor("FFFF77");
        public readonly Color ColorChannelHeaderPrivate = Calc.HexToColor("DDDD88") * 0.6f;
        public static readonly Color DefaultLevelColor = Color.LightGray;

        private static readonly char[] RandomizerEndTrimChars = "_0123456789".ToCharArray();

        public bool Active;

        private List<Blob> List = new();
        private Vector2 SizeAll;

        public DataChannelList Channels;

        public ListModes ListMode => Settings.PlayerListMode;
        private ListModes LastListMode;

        public LocationModes LocationMode => Settings.ShowPlayerListLocations;
        private LocationModes LastLocationMode;

        private float? SpaceWidth;
        private float? LocationSeparatorWidth;
        private float? IdleIconWidth;

        public enum ListModes {
            Channels,
            Classic,
        }

        [Flags]
        public enum LocationModes {
            OFF     = 0b00,
            Icons   = 0b01,
            Text    = 0b10,
            ON      = 0b11,
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
                new() {
                    Name = $"{all.Length} player{(all.Length == 1 ? "" : "s")}",
                    Color = ColorCountHeader
                }
            };

            switch (ListMode) {
                case ListModes.Classic:
                    foreach (DataPlayerInfo player in all.OrderBy(p => GetOrderKey(p))) {
                        if (string.IsNullOrWhiteSpace(player.DisplayName))
                            continue;

                        BlobPlayer blob = new() {
                            Name = player.DisplayName
                        };

                        DataChannelList.Channel channel = Channels.List.FirstOrDefault(c => c.Players.Contains(player.ID));
                        if (channel != null && !string.IsNullOrEmpty(channel.Name))
                            blob.Name += $" #{channel.Name}";

                        if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                            GetState(blob, state);

                        list.Add(blob);
                    }

                    break;

                case ListModes.Channels:
                    HashSet<DataPlayerInfo> listed = new();

                    void AddChannel(DataChannelList.Channel channel, Color color, float scaleFactor) {
                        list.Add(new() {
                            Name = channel.Name,
                            Color = ColorChannelHeader
                        });

                        foreach (DataPlayerInfo player in channel.Players.Select(p => GetPlayerInfo(p)).OrderBy(p => GetOrderKey(p))) {
                            BlobPlayer blob = new() { ScaleFactor = scaleFactor };
                            listed.Add(ListPlayerUnderChannel(blob, player));
                            list.Add(blob);
                        }
                    }

                    DataChannelList.Channel own = Channels.List.FirstOrDefault(c => c.Players.Contains(Client.PlayerInfo.ID));
                    if (own != null)
                        AddChannel(own, ColorChannelHeaderOwn, 0.5f);

                    foreach (DataChannelList.Channel channel in Channels.List)
                        if (channel != own)
                            AddChannel(channel, ColorChannelHeader, 1f);

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

            PrepareRenderLayout(out float scale, out _, out _, out float spaceWidth, out float locationSeparatorWidth, out float idleIconWidth);

            foreach (Blob blob in list)
                blob.Generate(LocationMode);

            int textScaleTry = 0;
            float textScale = scale;
            RetryLineScale:

            Vector2 sizeAll = Vector2.Zero;

            foreach (Blob blob in list) {
                blob.DynScale = Calc.LerpClamp(scale, textScale, blob.ScaleFactor);
                blob.DynY = sizeAll.Y;

                Vector2 size = blob.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth);
                sizeAll.X = Math.Max(sizeAll.X, size.X);
                sizeAll.Y += size.Y + 10f * scale;

                if (((sizeAll.X + 100f * scale) > UI_WIDTH * 0.7f || (sizeAll.Y + 90f * scale) > UI_HEIGHT * 0.7f) && textScaleTry < 5) {
                    textScaleTry++;
                    textScale -= scale * 0.1f;
                    goto RetryLineScale;
                }
            }

            List = list;
            SizeAll = sizeAll;
        }

        private string GetOrderKey(DataPlayerInfo player) {
            if (player == null)
                return "9";

            if (Client.Data.TryGetBoundRef(player, out DataPlayerState state) && !string.IsNullOrEmpty(state?.SID))
                return $"0 {"0" + state.SID + (int) state.Mode} {player.FullName}";

            return $"8 {player.FullName}";
        }

        private DataPlayerInfo GetPlayerInfo(uint id) {
            if (Client.Data.TryGetRef(id, out DataPlayerInfo player) && !string.IsNullOrEmpty(player?.DisplayName))
                return player;
            return null;
        }

        private DataPlayerInfo ListPlayerUnderChannel(BlobPlayer blob, DataPlayerInfo player) {
            if (player != null) {
                blob.Name = player.DisplayName;

                if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                    GetState(blob, state);

                return player;

            } else {
                blob.Name = "?";
                return null;
            }
        }

        private void GetState(BlobPlayer blob, DataPlayerState state) {
            if (!string.IsNullOrWhiteSpace(state.SID)) {
                AreaData area = AreaDataExt.Get(state.SID);
                string chapter = area?.Name?.DialogCleanOrNull(Dialog.Languages["english"]) ?? state.SID;

                blob.Location.Color = DefaultLevelColor;
                blob.Location.TitleColor = Color.Lerp(area?.TitleBaseColor ?? Color.White, DefaultLevelColor, 0.5f);
                blob.Location.AccentColor = Color.Lerp(area?.TitleAccentColor ?? Color.White, DefaultLevelColor, 0.8f);

                blob.Location.Name = chapter;
                blob.Location.Side = ((char) ('A' + (int) state.Mode)).ToString();
                blob.Location.Level = state.Level;

                blob.Location.IsRandomizer = chapter.StartsWith("randomizer/");

                if (blob.Location.IsRandomizer || area == null) {
                    blob.Location.Icon = "";
                } else {
                    blob.Location.Icon = area?.Icon ?? "";

                    string lobbySID = area?.GetMeta()?.Parent;
                    AreaData lobby = string.IsNullOrEmpty(lobbySID) ? null : AreaData.Get(lobbySID);
                    if (lobby?.Icon != null)
                        blob.Location.Icon = lobby.Icon;
                }

                ShortenRandomizerLocation(ref blob.Location);
            }

            blob.Idle = state.Idle;

            // Allow mods to override f.e. the displayed location name or icon very easily.
            OnGetState?.Invoke(blob, state);
        }

        private void ShortenRandomizerLocation(ref BlobLocation location) {
            /*
             * Randomizer Locations usually are very long like
             * Celeste/1-ForsakenCity/A/b-02/31 randomizer/Mirror Temple_0_1234567 A
             */

            if (!location.IsRandomizer || !Settings.PlayerListShortenRandomizer)
                return;

            // shorten the randomizer/ part down
            int split = location.Name.IndexOf('/');
            if (split >= 0)
                location.Name = "rnd/" + location.Name.Substring(split + 1);

            // yoink out all the funny numbers like _0_1234567 at the end
            location.Name = location.Name.TrimEnd(RandomizerEndTrimChars);

            // Only display the last two parts of the currently played level.
            split = location.Level.LastIndexOf('/');
            if ((split - 1) >= 0)
                split = location.Level.LastIndexOf('/', split - 1);
            if (split >= 0)
                location.Level = location.Level.Substring(split + 1);

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

            if (LastListMode != ListMode ||
                LastLocationMode != LocationMode) {
                LastListMode = ListMode;
                LastLocationMode = LocationMode;
                RebuildList();
            }

            if (!(Engine.Scene?.Paused ?? false) && Settings.ButtonPlayerList.Button.Pressed)
                Active = !Active;
        }

        public override void Draw(GameTime gameTime) {
            if (Active)
                base.Draw(gameTime);
        }

        protected void PrepareRenderLayout(out float scale, out float y, out Vector2 sizeAll, out float spaceWidth, out float locationSeparatorWidth, out float idleIconWidth) {
            scale = Scale;
            y = 50f * scale;
            sizeAll = SizeAll;
            spaceWidth = SpaceWidth ??= CelesteNetClientFont.Measure(" ").X;
            locationSeparatorWidth = LocationSeparatorWidth ??= CelesteNetClientFont.Measure(BlobLocation.LocationSeparator).X;
            idleIconWidth = IdleIconWidth ??= CelesteNetClientFont.Measure(BlobPlayer.IdleIconCode).X + spaceWidth;

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
        }

        protected override void Render(GameTime gameTime, bool toBuffer) {
            PrepareRenderLayout(out float scale, out float y, out Vector2 sizeAll, out float spaceWidth, out float locationSeparatorWidth, out _);

            Context.RenderHelper.Rect(25f * scale, y - 25f * scale, sizeAll.X + 50f * scale, sizeAll.Y + 40f * scale, Color.Black * 0.8f);

            foreach (Blob blob in List)
                blob.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth);

        }

        public class Blob {

            public string TextCached = "";

            public string Name = "";

            public Color Color = Color.White;

            public float ScaleFactor = 0f;
            public float DynY;
            public float DynScale;

            public virtual void Generate(LocationModes locationMode) {
                if (GetType() == typeof(Blob)) {
                    TextCached = Name;
                    return;
                }

                StringBuilder sb = new();
                Generate(sb, locationMode);
                TextCached = sb.ToString();
            }

            protected virtual void Generate(StringBuilder sb, LocationModes locationMode) {
                sb.Append(Name);
            }

            public virtual Vector2 Measure(float spaceWidth, float locationSeparatorWidth, float idleIconWidth) {
                return CelesteNetClientFont.Measure(TextCached) * DynScale;
            }

            public virtual void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth) {
                CelesteNetClientFont.Draw(
                    TextCached,
                    new(50f * scale, y + DynY),
                    Vector2.Zero,
                    new(DynScale),
                    Color
                );
            }

        }

        public class BlobPlayer : Blob {

            public const string IdleIconCode = ":celestenet_idle:";

            public BlobLocation Location = new();

            public bool Idle;

            protected override void Generate(StringBuilder sb, LocationModes locationMode) {
                sb.Append(Name);
                if (Idle)
                    sb.Append(" ").Append(IdleIconCode);

                // If the player blob was forced to regenerate its text, forward that to the location blob too.
                Location.Generate(locationMode);
            }

            public override Vector2 Measure(float spaceWidth, float locationSeparatorWidth, float idleIconWidth) {
                Location.DynY = DynY;
                Location.DynScale = DynScale;

                Vector2 size = base.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth);

                // insert extra space for the idle icon on non-idle players too.
                if (!Idle)
                    size.X += idleIconWidth * DynScale;

                size.X += Location.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth).X;

                return size;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth) {
                base.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth);
                Location.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth);
            }

        }

        public class BlobLocation : Blob {

            public const string LocationSeparator = ":";

            public MTexture GuiIconCached;

            public float IconSize => GuiIconCached != null ? 64f : 0f;
            public Vector2 IconOrigSize => GuiIconCached != null ? new Vector2(GuiIconCached.Width, GuiIconCached.Height) : new();
            public float IconScale => GuiIconCached != null ? Math.Min(IconSize / GuiIconCached.Width, IconSize / GuiIconCached.Height) : 1f;

            private float NameWidthScaled;
            public string Side = "";
            private float SideWidthScaled;
            public string Level = "";
            private float LevelWidthScaled;
            public string Icon = "";

            public bool IsRandomizer;

            public Color TitleColor = DefaultLevelColor;
            public Color AccentColor = DefaultLevelColor;

            public BlobLocation() {
                Color = DefaultLevelColor;
            }

            public override void Generate(LocationModes locationMode) {
                GuiIconCached = (locationMode & LocationModes.Icons) != 0 && GFX.Gui.Has(Icon) ? GFX.Gui[Icon] : null;
                if ((locationMode & LocationModes.Text) == 0)
                    Name = "";
            }

            public override Vector2 Measure(float spaceWidth, float locationSeparatorWidth, float idleIconWidth) {
                if (string.IsNullOrEmpty(Name))
                    return new(GuiIconCached != null ? IconSize * DynScale : 0f);

                float space = spaceWidth * DynScale;
                Vector2 size = CelesteNetClientFont.Measure(Name) * DynScale;
                NameWidthScaled = size.X;
                SideWidthScaled = CelesteNetClientFont.Measure(Side).X * DynScale;
                LevelWidthScaled = CelesteNetClientFont.Measure(Level).X * DynScale;
                return new(LevelWidthScaled + space + locationSeparatorWidth + space + NameWidthScaled + space + SideWidthScaled + (GuiIconCached != null ? space + IconSize * DynScale : 0f));
            }

            private void DrawTextPart(string text, float textWidthScaled, Color color, float y, float scale, ref float x) {
                CelesteNetClientFont.Draw(
                    text,
                    new(50f * scale + x, y + DynY),
                    Vector2.UnitX, // Rendering location bits right-to-left
                    new(DynScale),
                    color
                );
                x -= textWidthScaled;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth) {
                if (!string.IsNullOrEmpty(Name)) {
                    float space = spaceWidth * DynScale;
                    float x = sizeAll.X;
                    // Rendering location bits right-to-left
                    if (GuiIconCached != null) {
                        x -= IconSize * DynScale;
                        x -= space;
                    }
                    DrawTextPart(Side, SideWidthScaled, AccentColor, y, scale, ref x);
                    x -= space;
                    DrawTextPart(Name, NameWidthScaled, TitleColor, y, scale, ref x);
                    x -= space;
                    DrawTextPart(LocationSeparator, locationSeparatorWidth * DynScale, Color.Lerp(Color, Color.Black, 0.5f), y, scale, ref x);
                    x -= space;
                    DrawTextPart(Level, LevelWidthScaled, Color, y, scale, ref x);
                }

                GuiIconCached?.Draw(
                    new(50f * scale + sizeAll.X - IconSize * DynScale, y + DynY),
                    Vector2.Zero,
                    Color.White,
                    new Vector2(IconScale * DynScale)
                );
            }

        }

    }
}
