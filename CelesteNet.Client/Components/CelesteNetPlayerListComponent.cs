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

        public ListModes ListMode => Settings.PlayerListMode;
        private ListModes LastListMode;

        public LocationModes LocationMode => Settings.ShowPlayerListLocations;
        private LocationModes LastLocationMode;

        private Vector2? SizeSpace;
        private Vector2? SizeIdleIcon;

        public enum ListModes {
            Channels,
            Classic
        }

        public enum LocationModes {
            OFF,
            Icons,
            Text,
            ON
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


            switch (ListMode) {
                case ListModes.Classic:
                    foreach (DataPlayerInfo player in all.OrderBy(p => GetOrderKey(p))) {
                        if (string.IsNullOrWhiteSpace(player.DisplayName))
                            continue;

                        BlobPlayer blob = new();

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

                case ListModes.Channels:
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


            switch (LocationMode) {
                case LocationModes.OFF:
                    foreach (Blob blob in list) {
                        if (blob is BlobPlayer p) {
                            p.Location.Name = "";
                            p.Location.Icon = "";
                        }
                    }
                    break;

                case LocationModes.Icons:
                    foreach (Blob blob in list) {
                        if (blob is BlobPlayer p) {
                            p.Location.Name = "";
                        }
                    }
                    break;

                case LocationModes.Text:
                    foreach (Blob blob in list) {
                        if (blob is BlobPlayer p) {
                            p.Location.Icon = "";
                        }
                    }
                    break;

                case LocationModes.ON:
                default:
                    break;
            }

            List = list;
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

                blob.Location.Name = chapter;
                blob.Location.Side = ((char) ('A' + (int) state.Mode)).ToString();
                blob.Location.Level = state.Level;

                blob.Location.IsRandomizer = chapter.StartsWith("rnd/") || chapter.StartsWith("randomizer/");

                if (blob.Location.IsRandomizer || area == null) {
                    blob.Location.Icon = "";

                } else {
                    blob.Location.Icon = area?.Icon ?? "";

                    string lobbySID = area?.GetMeta()?.Parent;
                    AreaData lobby = string.IsNullOrEmpty(lobbySID) ? null : AreaDataExt.Get(lobbySID);
                    if (lobby == null) {
                        // fallback on string hacks
                        string[] areaPath = state.SID.Split('/');
                        if (areaPath.Length >= 3) {
                            lobby = AreaDataExt.Get(areaPath[0] + "/0-Lobbies/" + areaPath[1]);
                        }
                    }

                    if (lobby?.Icon != null)
                        blob.Location.Icon = lobby.Icon;
                }

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

            foreach (Blob blob in list)
                blob.GenerateText();

            int textScaleTry = 0;
            float textScale = scale;
            RetryLineScale:

            Vector2 sizeAll = Vector2.Zero;
            Vector2 sizeSpace = SizeSpace ??= CelesteNetClientFont.Measure(" ");
            Vector2 sizeIdleIcon = SizeIdleIcon ??= CelesteNetClientFont.Measure(BlobPlayer.IdleIconCode);

            foreach (Blob blob in list) {
                blob.DynScale = Calc.LerpClamp(scale, textScale, blob.ScaleFactor);
                blob.DynY = sizeAll.Y;

                Vector2 size = blob.Measure(ref sizeSpace, ref sizeIdleIcon);
                sizeAll.X = Math.Max(sizeAll.X, size.X);
                sizeAll.Y += size.Y + 10f * scale;

                if (((sizeAll.X + 100f * scale) > UI_WIDTH * 0.7f || (sizeAll.Y + 90f * scale) > UI_HEIGHT * 0.7f) && textScaleTry < 5) {
                    textScaleTry++;
                    textScale -= scale * 0.1f;
                    goto RetryLineScale;
                }
            }

            Context.RenderHelper.Rect(25f * scale, y - 25f * scale, sizeAll.X + 50f * scale, sizeAll.Y + 40f * scale, Color.Black * 0.8f);

            foreach (Blob blob in list)
                blob.Render(y, scale, ref sizeAll, ref sizeSpace);

        }

        public class Blob {

            public string TextCached = "";

            public string Name = "";

            public Color Color = Color.White;

            public float ScaleFactor = 0f;
            public float DynY;
            public float DynScale;

            public virtual void GenerateText() {
                if (GetType() == typeof(Blob)) {
                    TextCached = Name;
                    return;
                }

                StringBuilder sb = new();
                GenerateText(sb);
                TextCached = sb.ToString();
            }

            protected virtual void GenerateText(StringBuilder sb) {
                sb.Append(Name);
            }

            public virtual Vector2 Measure(ref Vector2 sizeSpace, ref Vector2 sizeIdleIcon) {
                return CelesteNetClientFont.Measure(TextCached) * DynScale;
            }

            public virtual void Render(float y, float scale, ref Vector2 sizeAll, ref Vector2 sizeSpace) {
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

            public const string IdleIconCode = " :celestenet_idle:";

            public BlobLocation Location = new();

            public bool Idle;

            protected override void GenerateText(StringBuilder sb) {
                sb.Append(Name);
                if (Idle)
                    sb.Append(IdleIconCode);

                // If the player blob was forced to regenerate its text, forward that to the location blob too.
                Location.GenerateText();
            }

            public override Vector2 Measure(ref Vector2 sizeSpace, ref Vector2 sizeIdleIcon) {
                Location.DynY = DynY;
                Location.DynScale = DynScale;

                Vector2 size = base.Measure(ref sizeSpace, ref sizeIdleIcon);

                // insert extra space for the idle icon on non-idle players too.
                if (!Idle)
                    size.X += sizeIdleIcon.X * DynScale;

                // Adjust for Randomizer locations getting shrunk
                size.X += Location.Measure(ref sizeSpace, ref sizeIdleIcon).X;

                return size;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, ref Vector2 sizeSpace) {
                base.Render(y, scale, ref sizeAll, ref sizeSpace);
                Location.Render(y, scale, ref sizeAll, ref sizeSpace);
            }

        }

        public class BlobLocation : Blob {


            public MTexture GuiIconCached;
            public Vector2 IconSize => new(GuiIconCached != null ? 64f : 0f);
            public Vector2 IconOrigSize => GuiIconCached != null ? new Vector2(GuiIconCached.Width, GuiIconCached.Height) : new();
            public float IconScale => GuiIconCached != null ? Math.Min(IconSize.X / GuiIconCached.Width, IconSize.Y / GuiIconCached.Height) : 1f;

            public string Side = "";
            public string Level = "";
            public string Icon = "";
            public bool IsRandomizer;

            public Color TitleColor = DefaultLevelColor;
            public Color AccentColor = DefaultLevelColor;

            public BlobLocation() {
                Color = DefaultLevelColor;
            }

            protected override void GenerateText(StringBuilder sb) {
                GuiIconCached = (GFX.Gui.Has(Icon) ? GFX.Gui.GetAtlasSubtexturesAt(Icon, 0) : null);

                if (!string.IsNullOrEmpty(Name)) {
                    sb.Append(Name);

                    if (!string.IsNullOrEmpty(Side))
                        sb.Append(" ").Append(Side);

                    if (!string.IsNullOrEmpty(Level))
                        sb.Append(" : ").Append(Level);
                }
            }

            public override Vector2 Measure(ref Vector2 sizeSpace, ref Vector2 sizeIdleIcon) {
                return base.Measure(ref sizeSpace, ref sizeIdleIcon) + new Vector2(sizeSpace.X + IconSize.X, 0) * DynScale;
            }

            private void DrawTextPart(string text, Color color, float y, float scale, ref float x, ref Vector2 sizeSpace) {
                CelesteNetClientFont.Draw(
                    text,
                    new(50f * scale + x, y + DynY),
                    Vector2.UnitX, // Rendering location bits right-to-left
                    new(DynScale),
                    color
                );
                x -= (CelesteNetClientFont.Measure(text).X + sizeSpace.X) * DynScale;
            }

            public override void Render(float y, float scale, ref Vector2 sizeAll, ref Vector2 sizeSpace) {
                Vector2 blobScale = new(DynScale);

                if (!string.IsNullOrEmpty(Name)) {
                    float x = sizeAll.X - IconSize.X * blobScale.X;
                    // Rendering location bits right-to-left
                    DrawTextPart(Side, AccentColor, y, scale, ref x, ref sizeSpace);
                    DrawTextPart(Name, TitleColor, y, scale, ref x, ref sizeSpace);
                    DrawTextPart(":", Color.Lerp(Color, Color.Black, 0.5f), y, scale, ref x, ref sizeSpace);
                    DrawTextPart(Level, Color, y, scale, ref x, ref sizeSpace);
                }

                GuiIconCached?.Draw(
                    new(50f * scale + sizeAll.X - IconSize.X * blobScale.X, y + DynY),
                    Vector2.Zero,
                    Color.White,
                    IconScale * blobScale
                );
            }

        }

    }
}
