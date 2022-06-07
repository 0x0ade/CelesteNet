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
        public bool ShouldRebuild = false;

        private List<Blob> List = new();

        private Vector2 SizeAll;
        private Vector2 SizeUpper;
        private Vector2 SizeColumn;

        /*
         SizeAll - outer dimensions
        +------------------------------------+
        |                                    |
        |            own channel             |
        |                                    |
        |             SizeUpper              |
        |                                    |
        +-----------------+------------------+
        |                 |                  |
        |                 |                  |
        |   SizeColumn    |  (calculated     |
        |                 |   from other     |
        |                 |   three sizes)   |
        |                 |                  |
        |(extends all ->  |                  |
        | the way right   |                  |
        | if single-col)  |                  |
        +-----------------+------------------+
        When not in Channel Mode, only SizeAll gets used.
         */

        public DataChannelList Channels;

        public ListModes ListMode => Settings.PlayerListMode;
        private ListModes LastListMode;

        public LocationModes LocationMode => Settings.ShowPlayerListLocations;
        private LocationModes LastLocationMode;

        public bool ShowPing => Settings.PlayerListShowPing;
        private bool LastShowPing;

        private float? SpaceWidth;
        private float? LocationSeparatorWidth;
        private float? IdleIconWidth;

        private int SplittablePlayerCount = 0;

        private readonly int SplitThresholdLower = 10;
        private readonly int SplitThresholdUpper = 12;

        private bool _splitViewPartially = false;
        private bool SplitViewPartially {
            get {
                if (ListMode != ListModes.Channels || !Settings.PlayerListAllowSplit)
                    return _splitViewPartially = false;
                // only flip value after passing threshold to prevent flipping on +1/-1s at threshold
                if (!_splitViewPartially && SplittablePlayerCount > SplitThresholdUpper)
                    return _splitViewPartially = true;
                if (_splitViewPartially && SplittablePlayerCount < SplitThresholdLower)
                    return _splitViewPartially = false;

                return _splitViewPartially;
            }
        }
        private bool SplitSuccessfully = false;

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
                    Color = ColorCountHeader,
                    ScaleFactor = 0.25f
                }
            };

            switch (ListMode) {
                case ListModes.Classic:
                    RebuildListClassic(ref list, ref all);
                    break;

                case ListModes.Channels:
                    RebuildListChannels(ref list, ref all);
                    break;
            }

            List = list;
        }

        public void RebuildListClassic(ref List<Blob> list, ref DataPlayerInfo[] all) {
            foreach (DataPlayerInfo player in all.OrderBy(p => GetOrderKey(p))) {
                if (string.IsNullOrWhiteSpace(player.DisplayName))
                    continue;

                BlobPlayer blob = new() {
                    Player = player,
                    Name = player.DisplayName,
                    ScaleFactor = 0.75f
                };

                DataChannelList.Channel channel = Channels.List.FirstOrDefault(c => c.Players.Contains(player.ID));
                if (channel != null && !string.IsNullOrEmpty(channel.Name))
                    blob.Name += $" #{channel.Name}";

                if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                    GetState(blob, state);

                list.Add(blob);
            }

            PrepareRenderLayout(out float scale, out _, out _, out float spaceWidth, out float locationSeparatorWidth, out float idleIconWidth);

            foreach (Blob blob in list)
                blob.Generate();

            int textScaleTry = 0;
            float textScale = scale;
            RetryLineScale:

            Vector2 sizeAll = Vector2.Zero;

            foreach (Blob blob in list) {
                blob.DynScale = Calc.LerpClamp(scale, textScale, blob.ScaleFactor);
                blob.Dyn.Y = sizeAll.Y;

                Vector2 size = blob.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth);
                sizeAll.X = Math.Max(sizeAll.X, size.X);
                sizeAll.Y += size.Y + 10f * scale;

                if (((sizeAll.X + 100f * scale) > UI_WIDTH * 0.7f || (sizeAll.Y + 90f * scale) > UI_HEIGHT * 0.7f) && textScaleTry < 5) {
                    textScaleTry++;
                    textScale -= scale * 0.1f;
                    goto RetryLineScale;
                }
            }

            SizeAll = sizeAll;
            SizeUpper = sizeAll;
            SizeColumn = Vector2.Zero;
        }

        public void RebuildListChannels(ref List<Blob> list, ref DataPlayerInfo[] all) {
            // this value gets updated at every blob that we could split at
            // i.e. every channel header besides our own.
            int lastPossibleSplit = 0;

            HashSet<DataPlayerInfo> listed = new();
            DataChannelList.Channel own = Channels.List.FirstOrDefault(c => c.Players.Contains(Client.PlayerInfo.ID));

            void AddChannel(ref List<Blob> list, DataChannelList.Channel channel, Color color, float scaleFactorHeader, float scaleFactor, LocationModes locationMode) {
                list.Add(new() {
                    Name = channel.Name,
                    Color = ColorChannelHeader,
                    ScaleFactor = scaleFactorHeader,
                    CanSplit = channel != own
                });
                if (channel != own)
                    lastPossibleSplit = list.Count - 1;

                foreach (DataPlayerInfo player in channel.Players.Select(p => GetPlayerInfo(p)).OrderBy(p => GetOrderKey(p))) {
                    BlobPlayer blob = new() { ScaleFactor = scaleFactor };
                    listed.Add(ListPlayerUnderChannel(blob, player, locationMode));
                    list.Add(blob);
                }
            }

            SplittablePlayerCount = all.Length;

            if (own != null) {
                SplittablePlayerCount -= own.Players.Length;
                AddChannel(ref list, own, ColorChannelHeaderOwn, 0.25f, 0.5f, LocationMode);
            }
            // this is the index of the first element relevant for splitting,
            // i.e. first blob after our own channel is fully listed.
            int splitStartsAt = list.Count - 1;

            foreach (DataChannelList.Channel channel in Channels.List)
                if (channel != own)
                    AddChannel(ref list, channel, ColorChannelHeader, 0.75f, 1f, LocationModes.OFF);

            bool wrotePrivate = false;
            foreach (DataPlayerInfo player in all.OrderBy(p => GetOrderKey(p))) {
                if (listed.Contains(player) || string.IsNullOrWhiteSpace(player.DisplayName))
                    continue;

                if (!wrotePrivate) {
                    wrotePrivate = true;
                    list.Add(new() {
                        Name = "!<private>",
                        Color = ColorChannelHeaderPrivate,
                        ScaleFactor = 0.75f,
                        CanSplit = true
                    });
                    lastPossibleSplit = list.Count - 1;
                }

                list.Add(new() {
                    Name = player.DisplayName,
                    ScaleFactor = 1f
                });
            }

            // if nothing was actually added after recording splitStartsAt, reset it to 0 (nothing to split up)
            splitStartsAt = list.Count > splitStartsAt + 1 ? splitStartsAt + 1 : 0;

            PrepareRenderLayout(out float scale, out _, out _, out float spaceWidth, out float locationSeparatorWidth, out float idleIconWidth);

            foreach (Blob blob in list)
                blob.Generate();

            int textScaleTry = 0;
            float textScale = scale;
            RetryLineScale:

            Vector2 sizeAll = Vector2.Zero;
            Vector2 sizeToSplit = Vector2.Zero;
            Vector2 sizeUpper = Vector2.Zero;

            for (int i = 0; i < list.Count; i++) {
                Blob blob = list[i];
                blob.DynScale = Calc.LerpClamp(scale, textScale, blob.ScaleFactor);
                blob.Dyn.Y = sizeAll.Y;

                // introducing gap after own channel
                if (splitStartsAt > 0 && i == splitStartsAt) {
                    sizeUpper = sizeAll;
                    blob.Dyn.Y += 30f * scale;
                    sizeAll.Y += 30f * scale;
                }

                Vector2 size = blob.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth);
                // proceed as we usually did if not splitting or before split starts
                if (!SplitViewPartially || splitStartsAt == 0 || i < splitStartsAt) {
                    sizeAll.X = Math.Max(sizeAll.X, size.X);
                    sizeAll.Y += size.Y + 10f * scale;
                } else {
                    // ... otherwise we record the sizes seperately
                    sizeToSplit.X = Math.Max(sizeToSplit.X, size.X);
                    sizeToSplit.Y += size.Y + 10f * scale;
                }

                if (((Math.Max(sizeAll.X, sizeToSplit.X) + 100f * scale) > UI_WIDTH * 0.7f || (sizeAll.Y + sizeToSplit.Y / 2f + 90f * scale) > UI_HEIGHT * 0.7f) && textScaleTry < 5) {
                    textScaleTry++;
                    textScale -= scale * 0.1f;
                    goto RetryLineScale;
                }
            }

            if (splitStartsAt == 0)
                sizeUpper = sizeAll;

            int forceSplitAt = -1;
            RetryPartialSplit:
            bool splitSuccessfully = false;
            Vector2 sizeColumn = Vector2.Zero;
            float maxColumnY = 0f;
            
            int switchedSidesAt = 0;
            if (SplitViewPartially && splitStartsAt > 0) {
                for (int i = splitStartsAt; i < list.Count; i++) {
                    Blob blob = list[i];

                    Vector2 size = blob.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth);
                    sizeColumn.X = Math.Max(sizeColumn.X, size.X);

                    // have we reached half the splittable height or enforced a split?
                    if (!splitSuccessfully && (sizeColumn.Y > sizeToSplit.Y / 2f + 10f || forceSplitAt == i)) {
                        if (blob.CanSplit) {
                            switchedSidesAt = i;
                            maxColumnY = sizeColumn.Y;
                            sizeColumn.Y = 0f;
                            splitSuccessfully = true;
                        } else if (lastPossibleSplit > splitStartsAt && i > lastPossibleSplit && forceSplitAt == -1) {
                            // this is for cases where the last possible split was before the half-way point in height; forcing with a "goto retry"
                            forceSplitAt = lastPossibleSplit;
                            list[lastPossibleSplit].CanSplit = true;
                            goto RetryPartialSplit;
                        }
                    }
                    blob.Dyn.Y = sizeAll.Y + sizeColumn.Y;
                    sizeColumn.Y += size.Y + 10f * scale;
                }

                if (splitSuccessfully) {

                    if (sizeColumn.X * 2f < sizeAll.X) {
                        sizeColumn.X = sizeAll.X / 2f;
                    } else {
                        // some padding for when the individual rects get drawn later
                        sizeColumn.X += 30f * scale;
                    }


                    // move all the right column's elements to the right via Dyn.X
                    for (int i = switchedSidesAt; i < list.Count; i++)
                        list[i].Dyn.X = sizeColumn.X + 15f;
                }

                if (sizeColumn.Y > maxColumnY)
                    maxColumnY = sizeColumn.Y;

                sizeAll.Y += maxColumnY;
                sizeAll.X = Math.Max(sizeAll.X, sizeColumn.X * 2f);
            }

            SizeAll = sizeAll;
            SizeUpper = sizeUpper;
            SizeColumn = new(sizeColumn.X, maxColumnY);
            SplitSuccessfully = splitSuccessfully;
        }

        private string GetOrderKey(DataPlayerInfo player) {
            if (player == null)
                return "9";

            if (Client.Data.TryGetBoundRef(player, out DataPlayerState state) && !string.IsNullOrEmpty(state?.SID))
                return $"0 {(state.SID.StartsWith("Celeste/") ? "0" : "1") + state.SID + (int) state.Mode} {player.FullName}";

            return $"8 {player.FullName}";
        }

        private DataPlayerInfo GetPlayerInfo(uint id) {
            if (Client.Data.TryGetRef(id, out DataPlayerInfo player) && !string.IsNullOrEmpty(player?.DisplayName))
                return player;
            return null;
        }

        private DataPlayerInfo ListPlayerUnderChannel(BlobPlayer blob, DataPlayerInfo player, LocationModes locationMode) {
            if (player != null) {
                blob.Player = player;
                blob.Name = player.DisplayName;

                blob.LocationMode = locationMode;
                if (locationMode != LocationModes.OFF && Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                    GetState(blob, state);

                return player;

            } else {
                blob.Name = "?";
                blob.LocationMode = LocationModes.OFF;
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
            RunOnMainThread(() => RebuildList());
        }

        public void Handle(CelesteNetConnection con, DataPlayerState state) {
            RunOnMainThread(() => RebuildList());
        }

        public void Handle(CelesteNetConnection con, DataConnectionInfo info) {
            RunOnMainThread(() => {
                if (!ShowPing)
                    return;

                // Don't rebuild the entire list
                // Try to find the player's blob
                BlobPlayer playerBlob = (BlobPlayer) List?.FirstOrDefault(b => b is BlobPlayer pb && pb.Player == info.Player);
                if (playerBlob == null)
                    return;

                // Update the player's ping
                playerBlob.PingMs = info.UDPPingMs ?? info.TCPPingMs;

                // Regenerate the player blob
                playerBlob.Generate();

                // Re-measure the list
                // This doesn't handle line splitting/etc, but is good enough
                if (!SpaceWidth.HasValue || !LocationSeparatorWidth.HasValue || !IdleIconWidth.HasValue) {
                    // This should never happen, as the list has already been rendered at least once
                    // Still check just in case
                    Logger.Log(LogLevel.WRN, "playerlist", "!!!DEAD CODE REACHED!!! Player list layout values still uninitalized in ping update code!");
                    return;
                }

                Vector2 sizeAll = Vector2.Zero;
                foreach (Blob blob in List) {
                    Vector2 size = blob.Measure(SpaceWidth.Value, LocationSeparatorWidth.Value, IdleIconWidth.Value);
                    sizeAll.X = Math.Max(sizeAll.X, size.X);
                    sizeAll.Y += size.Y + 10f * Scale;
                }
                SizeAll = sizeAll;
                SizeUpper = sizeAll;
                SizeColumn = Vector2.Zero;
            });
        }

        public void Handle(CelesteNetConnection con, DataChannelList channels) {
            RunOnMainThread(() => {
                Channels = channels;
                RebuildList();
            });
        }
        
        public void Handle(CelesteNetConnection con, DataNetEmoji netemoji) {
            if (!netemoji.MoreFragments)
                ShouldRebuild = true;
        }

        public override void Update(GameTime gameTime) {
            base.Update(gameTime);

            if (LastListMode != ListMode ||
                LastLocationMode != LocationMode ||
                LastShowPing != ShowPing ||
                ShouldRebuild) {
                LastListMode = ListMode;
                LastLocationMode = LocationMode;
                LastShowPing = ShowPing;
                ShouldRebuild = false;
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

            float x = 25f * scale;
            float sizeAllXPadded = sizeAll.X + 50f * scale;
            float chatStartY = (Context?.Chat?.RenderPositionY ?? UI_HEIGHT) - 5f;
            Color colorFull = Color.Black * 0.8f;
            Color colorFaded = Color.Black * 0.5f;

            switch (ListMode) {
                case ListModes.Classic:
                    SplitRectAbsolute(x, y - 25f * scale, sizeAllXPadded, sizeAll.Y + 30f * scale, chatStartY, colorFull, colorFaded);
                    break;

                case ListModes.Channels:
                    // own channel box always there
                    SplitRectAbsolute(x, y - 25f * scale, sizeAllXPadded, SizeUpper.Y + 30f * scale, chatStartY, colorFull, colorFaded);

                    if (SplitViewPartially && SplitSuccessfully) {
                        // two rects for the two columns
                        float sizeColXPadded = SizeColumn.X + 25f * scale;
                        SplitRectAbsolute(x, y + SizeUpper.Y + 15f * scale, sizeColXPadded - 5f * scale, sizeAll.Y - SizeUpper.Y + 30f * scale, chatStartY, colorFull, colorFaded);
                        x += sizeColXPadded + 5f * scale;
                        SplitRectAbsolute(x, y + SizeUpper.Y + 15f * scale, sizeAllXPadded - sizeColXPadded - 5f * scale, sizeAll.Y - SizeUpper.Y + 30f * scale, chatStartY, colorFull, colorFaded);
                    } else {
                        // single rect below the other, nothing was split after all
                        SplitRectAbsolute(x, y + SizeUpper.Y + 15f * scale, sizeAllXPadded, sizeAll.Y - SizeUpper.Y + 30f * scale, chatStartY, colorFull, colorFaded);
                    }
                    break;
            }

            float alpha;
            foreach (Blob blob in List) {
                alpha = (y + blob.Dyn.Y < chatStartY) ? 1f : 0.5f;
                blob.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth, alpha);
            }

        }

        private void SplitRectAbsolute(float x, float y, float width, float height, float splitAtY, Color colorA, Color colorB) {
            if (splitAtY > y + height) {
                Context.RenderHelper.Rect(x, y, width, height, colorA);
            }
            else if (splitAtY < y) {
                Context.RenderHelper.Rect(x, y, width, height, colorB);
            }
            else {
                SplitRect(x, y, width, height, splitAtY - y, colorA, colorB);
            }
        }

        private void SplitRect(float x, float y, float width, float height, float splitheight, Color colorA, Color colorB) {
            if (splitheight >= height) {
                Context.RenderHelper.Rect(x, y, width, height, colorA);
                return;
            }

            Context.RenderHelper.Rect(x, y, width, splitheight, colorA);
            Context.RenderHelper.Rect(x, y + splitheight, width, height - splitheight, colorB);
        }

        public class Blob {

            public string TextCached = "";

            public string Name = "";

            public Color Color = Color.White;

            public float ScaleFactor = 0f;
            public Vector2 Dyn = Vector2.Zero;
            public float DynScale;
            public bool CanSplit = false;

            public LocationModes LocationMode = LocationModes.ON;

            public virtual void Generate() {
                if (GetType() == typeof(Blob)) {
                    TextCached = Name;
                    return;
                }

                StringBuilder sb = new();
                Generate(sb);
                TextCached = sb.ToString();
            }

            protected virtual void Generate(StringBuilder sb) {
                sb.Append(Name);
            }

            public virtual Vector2 Measure(float spaceWidth, float locationSeparatorWidth, float idleIconWidth) {
                return CelesteNetClientFont.Measure(TextCached) * DynScale;
            }

            public virtual void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth, float alpha) {
                CelesteNetClientFont.Draw(
                    TextCached,
                    new(50f * scale + Dyn.X, y + Dyn.Y),
                    Vector2.Zero,
                    new(DynScale),
                    Color * alpha
                );
            }

        }

        public class BlobPlayer : Blob {

            public const string IdleIconCode = ":celestenet_idle:";

            public DataPlayerInfo Player;
            public BlobLocation Location = new();

            public int? PingMs = null;
            public Blob PingBlob = new() {
                Color = Color.Gray
            };

            public bool Idle;

            protected override void Generate(StringBuilder sb) {
                sb.Append(Name);
                if (Idle)
                    sb.Append(" ").Append(IdleIconCode);

                if (PingMs.HasValue) {
                    int ping = PingMs.Value;
                    if (0 < ping)
                        PingBlob.Name = $"{ping}ms";
                    else
                        PingBlob.Name = "SPOOFED!"; // Someone messed with the packets
                } else
                    PingBlob.Name = string.Empty;

                // If the player blob was forced to regenerate its text, forward that to the location and ping blobs too.
                PingBlob.Generate();
                Location.LocationMode = LocationMode;
                Location.Generate();
            }

            public override Vector2 Measure(float spaceWidth, float locationSeparatorWidth, float idleIconWidth) {

                Vector2 size = base.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth);

                // insert extra space for the idle icon on non-idle players too.
                if (!Idle)
                    size.X += idleIconWidth * DynScale;

                // update nested blobs
                PingBlob.Dyn.X = Dyn.X + size.X;
                PingBlob.Dyn.Y = Dyn.Y;
                PingBlob.DynScale = DynScale;
                Location.Dyn.Y = Dyn.Y;
                Location.DynScale = DynScale;
                Location.LocationMode = LocationMode;

                // insert space for ping and location
                size.X += PingBlob.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth).X;
                size.X += Location.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth).X;

                return size;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth, float alpha) {
                base.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth, alpha);
                PingBlob.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth, alpha);
                Location.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth, alpha);
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

            public override void Generate() {
                GuiIconCached = (LocationMode & LocationModes.Icons) != 0 && GFX.Gui.Has(Icon) ? GFX.Gui[Icon] : null;
                if ((LocationMode & LocationModes.Text) == 0)
                    Name = "";
            }

            public override Vector2 Measure(float spaceWidth, float locationSeparatorWidth, float idleIconWidth) {
                if (string.IsNullOrEmpty(Name) || (LocationMode & LocationModes.Text) == 0)
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
                    new(50f * scale + x, y + Dyn.Y),
                    Vector2.UnitX, // Rendering location bits right-to-left
                    new(DynScale),
                    color
                );
                x -= textWidthScaled;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth, float alpha) {
                if (!string.IsNullOrEmpty(Name)) {
                    float space = spaceWidth * DynScale;
                    float x = sizeAll.X + Dyn.X;
                    // Rendering location bits right-to-left
                    if (GuiIconCached != null) {
                        x -= IconSize * DynScale;
                        x -= space;
                    }
                    DrawTextPart(Side, SideWidthScaled, AccentColor * alpha, y, scale, ref x);
                    x -= space;
                    DrawTextPart(Name, NameWidthScaled, TitleColor * alpha, y, scale, ref x);
                    x -= space;
                    DrawTextPart(LocationSeparator, locationSeparatorWidth * DynScale, Color.Lerp(Color, Color.Black, 0.5f) * alpha, y, scale, ref x);
                    x -= space;
                    DrawTextPart(Level, LevelWidthScaled, Color * alpha, y, scale, ref x);
                }

                GuiIconCached?.Draw(
                    new(50f * scale + sizeAll.X + Dyn.X - IconSize * DynScale, y + Dyn.Y),
                    Vector2.Zero,
                    Color.White * alpha,
                    new Vector2(IconScale * DynScale)
                );
            }

        }

    }
}
