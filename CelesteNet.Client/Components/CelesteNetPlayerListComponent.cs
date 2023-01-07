﻿using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public bool AllowSplit => Settings.PlayerListAllowSplit;
        private bool LastAllowSplit;

        private float? SpaceWidth;
        private float? LocationSeparatorWidth;
        private float? IdleIconWidth;

        private int SplittablePlayerCount = 0;

        private readonly int SplitThresholdLower = 10;
        private readonly int SplitThresholdUpper = 12;

        private bool _splitViewPartially = false;
        private bool SplitViewPartially {
            get {
                if (ListMode != ListModes.Channels || !AllowSplit)
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
            if (MDraw.DefaultFont == null || Client?.PlayerInfo == null || Channels == null)
                return;

            BlobPlayer.PingPadWidth = 0;

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

                if (ShowPing && Client.Data.TryGetBoundRef(player, out DataConnectionInfo conInfo))
                    blob.PingMs = conInfo.UDPPingMs ?? conInfo.TCPPingMs;

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
                    listed.Add(ListPlayerUnderChannel(blob, player, locationMode, channel == own));
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

        private DataPlayerInfo ListPlayerUnderChannel(BlobPlayer blob, DataPlayerInfo player, LocationModes locationMode, bool withPing) {
            if (player != null) {
                blob.Player = player;
                blob.Name = player.DisplayName;

                blob.LocationMode = locationMode;
                if (locationMode != LocationModes.OFF && Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                    GetState(blob, state);

                if (ShowPing && withPing && Client.Data.TryGetBoundRef(player, out DataConnectionInfo conInfo))
                    blob.PingMs = conInfo.UDPPingMs ?? conInfo.TCPPingMs;

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

                blob.Location.SID = state.SID;
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
            RunOnMainThread(() => {
                if (MDraw.DefaultFont == null || Client?.PlayerInfo == null || Channels == null)
                    return;

                // Don't rebuild the entire list
                // Try to find the player's blob
                BlobPlayer playerBlob = (BlobPlayer) List?.FirstOrDefault(b => b is BlobPlayer pb && pb.Player == state.Player);
                if (playerBlob == null || playerBlob.Location.SID.IsNullOrEmpty() || playerBlob.Location.SID != state.SID || playerBlob.Location.Level.Length < state.Level.Length - 1) {
                    RebuildList();
                    return;
                }

                // just update blob state, since SID hasn't changed
                GetState(playerBlob, state);
                playerBlob.Generate();
            });

        }

        public void Handle(CelesteNetConnection con, DataConnectionInfo info) {
            RunOnMainThread(() => {
                if (!ShowPing)
                    return;

                if (MDraw.DefaultFont == null || Client?.PlayerInfo == null || Channels == null)
                    return;

                // Don't rebuild the entire list
                // Try to find the player's blob
                BlobPlayer playerBlob = (BlobPlayer) List?.FirstOrDefault(b => b is BlobPlayer pb && pb.Player == info.Player);
                if (playerBlob == null)
                    return;

                DataChannelList.Channel own = Channels.List.FirstOrDefault(c => c.Players.Contains(Client.PlayerInfo.ID));
                if (ListMode == ListModes.Channels && !own.Players.Contains(info.Player.ID))
                    return;

                PrepareRenderLayout(out float scale, out float y, out Vector2 sizeAll, out float spaceWidth, out float locationSeparatorWidth, out float idleIconWidth);

                // Update the player's ping
                playerBlob.PingMs = info.UDPPingMs ?? info.TCPPingMs;

                // Regenerate the player blob
                playerBlob.Generate();

                Vector2 size = playerBlob.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth);

                SizeAll.X = Math.Max(size.X, SizeAll.X);
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
                LastAllowSplit != AllowSplit ||
                ShouldRebuild) {
                LastListMode = ListMode;
                LastLocationMode = LocationMode;
                LastShowPing = ShowPing;
                LastAllowSplit = AllowSplit;
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

            public virtual void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth, float alpha, Vector2? justify = null) {
                CelesteNetClientFont.Draw(
                    TextCached,
                    new(50f * scale + Dyn.X, y + Dyn.Y),
                    justify ?? Vector2.Zero,
                    new(DynScale),
                    Color * alpha
                );
            }

        }

        public class BlobPlayer : Blob {

            public const string IdleIconCode = ":celestenet_idle:";
            public const string NoPingData = "???";

            public DataPlayerInfo Player;
            public BlobLocation Location = new();

            public int? PingMs = null;
            public Blob PingBlob = new() {
                Color = Color.Gray
            };
            public static float PingPadWidth = 0;

            public bool Idle;

            protected override void Generate(StringBuilder sb) {
                sb.Append(Name);
                if (Idle)
                    sb.Append(" ").Append(IdleIconCode);

                if (PingMs.HasValue) {
                    int ping = PingMs.Value;
                    PingBlob.Name = $"{(ping > 0 ? ping : NoPingData)}ms";
                } else {
                    PingBlob.Name = string.Empty;
                }

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

                // update ping blob
                PingBlob.Dyn.Y = Dyn.Y;
                PingBlob.DynScale = DynScale;

                // insert space for ping first, because it offsets location
                float pingWidth = PingBlob.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth).X;
                PingPadWidth = Math.Max(PingPadWidth, pingWidth);
                size.X += PingPadWidth;

                // update & insert space for location
                Location.Offset = -PingPadWidth;
                Location.Dyn.Y = Dyn.Y;
                Location.DynScale = DynScale;
                Location.LocationMode = LocationMode;
                size.X += Location.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth).X;

                return size;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth, float alpha, Vector2? justify = null) {
                base.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth, alpha, justify);
                Location.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth, alpha);
                // right-justify ping
                PingBlob.Dyn.X = Dyn.X + sizeAll.X;
                PingBlob.Render(y, scale, ref sizeAll, spaceWidth, locationSeparatorWidth, alpha, Vector2.UnitX);
            }

        }

        public class BlobRightToLeft : Blob {

            protected class TextPart {
                public string Text;
                public Color Color;
                public float widthScaled;
            }

            protected List<TextPart> parts = new ();

            public float Offset = 0;

            protected void AddTextPart(string content, Color color) {
                parts.Add(new TextPart { Text = content, Color = color });
            }

            public override Vector2 Measure(float spaceWidth, float locationSeparatorWidth, float idleIconWidth) {
                Vector2 size = new();

                foreach (TextPart p in parts) {
                    Vector2 measurement = CelesteNetClientFont.Measure(p.Text);
                    p.widthScaled = measurement.X * DynScale;
                    size.X += p.widthScaled;
                    if (measurement.Y > size.Y)
                        size.Y = measurement.Y;
                }
                size.X += spaceWidth * DynScale * parts.Count;

                return size;
            }

            protected void DrawTextPart(string text, float textWidthScaled, Color color, float y, float scale, ref float x) {
                CelesteNetClientFont.Draw(
                    text,
                    new(50f * scale + x, y + Dyn.Y),
                    Vector2.UnitX, // Rendering bits right-to-left
                    new(DynScale),
                    color
                );
                x -= textWidthScaled;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth, float alpha, Vector2? justify = null) {
                float x = sizeAll.X + Dyn.X + Offset;

                for (int i = parts.Count - 1; i >= 0; i--) {
                    TextPart p = parts[i];
                    DrawTextPart(p.Text, p.widthScaled, p.Color * alpha, y, scale, ref x);
                    x -= spaceWidth * DynScale;
                }
            }

        }


        public class BlobLocation : BlobRightToLeft {

            public const string LocationSeparator = ":";

            protected MTexture GuiIconCached;

            public float IconSize => GuiIconCached != null ? 64f : 0f;
            public Vector2 IconOrigSize => GuiIconCached != null ? new Vector2(GuiIconCached.Width, GuiIconCached.Height) : new();
            public float IconScale => GuiIconCached != null ? Math.Min(IconSize / GuiIconCached.Width, IconSize / GuiIconCached.Height) : 1f;

            public string Side = "";
            public string Level = "";
            public string Icon = "";

            public string SID = "";

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
                if (parts.Count < 4) {
                    parts.Clear();
                    AddTextPart(Level, Color);
                    AddTextPart(LocationSeparator, Color.Lerp(Color, Color.Black, 0.5f));
                    AddTextPart(Name, TitleColor);
                    AddTextPart(Side, AccentColor);
                } else {
                    parts[0].Text = Level;
                    parts[2].Text = Name;
                    parts[3].Text = Side;
                }
            }

            public override Vector2 Measure(float spaceWidth, float locationSeparatorWidth, float idleIconWidth) {
                if (string.IsNullOrEmpty(Name) || (LocationMode & LocationModes.Text) == 0)
                    return new(GuiIconCached != null ? IconSize * DynScale : 0f);

                float space = spaceWidth * DynScale;
                Vector2 size = base.Measure(spaceWidth, locationSeparatorWidth, idleIconWidth);
                if (GuiIconCached != null)
                    size.X += space + IconSize * DynScale;
                return size;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, float spaceWidth, float locationSeparatorWidth, float alpha, Vector2? justify = null) {
                if (!string.IsNullOrEmpty(Name)) {
                    float space = spaceWidth * DynScale;
                    Vector2 size = new(sizeAll.X + Dyn.X, sizeAll.Y);
                    if (GuiIconCached != null) {
                        size.X -= IconSize * DynScale;
                        size.X -= space;
                    }

                    base.Render(y, scale, ref size, spaceWidth, locationSeparatorWidth, alpha, justify);
                }

                GuiIconCached?.Draw(
                    new(50f * scale + sizeAll.X + Dyn.X - IconSize * DynScale + Offset, y + Dyn.Y),
                    Vector2.Zero,
                    Color.White * alpha,
                    new Vector2(IconScale * DynScale)
                );
            }

        }

    }
}
