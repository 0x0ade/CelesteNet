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

        public readonly Color[] ColorChapters = new Color[] {
            Calc.HexToColor("FF0000"),
            Calc.HexToColor("FF1111"),
            Calc.HexToColor("FF2222"),
            Calc.HexToColor("FF3333"),
            Calc.HexToColor("FF4444"),
            Calc.HexToColor("FF5555"),
            Calc.HexToColor("FF6666"),
            Calc.HexToColor("FF7777"),
            Calc.HexToColor("FF8888"),
            Calc.HexToColor("FF9999"),
        };

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
                    DisplayName = $"{all.Length} player{(all.Length == 1 ? "" : "s")}",
                    Color = ColorCountHeader
                }
            };


            switch (Mode) {
                case ListMode.Classic:
                    foreach (DataPlayerInfo player in all.OrderBy(p => GetOrderKey(p))) {
                        if (string.IsNullOrWhiteSpace(player.DisplayName))
                            continue;

                        Blob blob = new();

                        // TODO: Figure out proper way to get Avatar by itself?
                        // Player only has Name/FullName/DisplayName... with Avatar in the latter :(
                        blob.DisplayName = player.DisplayName;

                        DataChannelList.Channel channel = Channels.List.FirstOrDefault(c => c.Players.Contains(player.ID));
                        if (channel != null && !string.IsNullOrEmpty(channel.Name)) {
                            blob.DisplayName += $" #{channel.Name}";
                        }

                        if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                            GetState(blob, state);

                        list.Add(blob);
                    }

                    break;

                case ListMode.Channels:
                case ListMode.CompactChannels:
                    HashSet<DataPlayerInfo> listed = new();

                    DataChannelList.Channel own = Channels.List.FirstOrDefault(c => c.Players.Contains(Client.PlayerInfo.ID));

                    if (own != null) {
                        list.Add(new() {
                            DisplayName = own.Name,
                            Color = ColorChannelHeaderOwn
                        });

                        foreach (DataPlayerInfo player in own.Players.Select(p => GetPlayerInfo(p)).OrderBy(p => GetOrderKey(p))) {
                            Blob blob = new() { ScaleFactor = 0.5f };
                            listed.Add(ListPlayerUnderChannel(blob, player));

                            if (Mode == ListMode.CompactChannels)
                                blob.Location = "";
                            list.Add(blob);
                        }
                    }

                    foreach (DataChannelList.Channel channel in Channels.List) {
                        if (channel == own)
                            continue;

                        list.Add(new() {
                            DisplayName = channel.Name,
                            Color = ColorChannelHeader
                        });

                        foreach (DataPlayerInfo player in channel.Players.Select(p => GetPlayerInfo(p)).OrderBy(p => GetOrderKey(p))) {
                            Blob blob = new() { ScaleFactor = 1f };
                            listed.Add(ListPlayerUnderChannel(blob, player));

                            if (Mode == ListMode.CompactChannels)
                                blob.Location = "";
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
                                DisplayName = "!<private>",
                                Color = ColorChannelHeaderPrivate
                            });
                        }

                        list.Add(new() {
                            DisplayName = player.DisplayName,
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

        private DataPlayerInfo ListPlayerUnderChannel(Blob blob, DataPlayerInfo player) {
            if (player != null) {
                blob.DisplayName = player.DisplayName;

                if (Client.Data.TryGetBoundRef(player, out DataPlayerState state))
                    GetState(blob, state);

                return player;

            } else {
                blob.DisplayName = "?";
                return null;
            }
        }

        private void GetState(Blob blob, DataPlayerState state) {
            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(state.SID)) {
                string chapter = AreaDataExt.Get(state.SID)?.Name?.DialogCleanOrNull(Dialog.Languages["english"]) ?? state.SID;

                if (!Emoji.TryGet(chapter.ToLowerInvariant().Replace(' ', '_'), out char chaptericon))
                    chaptericon = '\0';

                blob.LocationIcon = chaptericon.ToString();

                builder
                    .Append(chapter)
                    .Append(" ")
                    .Append((char) ('A' + (int) state.Mode))
                    .Append(" ")
                    .Append(state.Level);
            }

            blob.Idle = state.Idle;

            blob.Location = builder.ToString();
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
                Vector2 size = CelesteNetClientFont.Measure(blob.Text) * blob.DynScale;
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
                string display = string.Join(" ",
                    new List<string>() {
                        blob.DisplayIcon,
                        blob.DisplayName,
                        blob.IdleIcon
                    }.Where(item => !string.IsNullOrEmpty(item)));

                CelesteNetClientFont.Draw(
                    display,
                    new(50f * scale, y + blob.DynY),
                    Vector2.Zero,
                    new(blob.DynScale, blob.DynScale),
                    blob.Color
                );

                if (!string.IsNullOrEmpty(blob.Location) || !string.IsNullOrEmpty(blob.LocationIcon)) {
                    CelesteNetClientFont.Draw(
                        blob.Location + " " + blob.LocationIcon,
                        new(50f * scale + sizeAll.X, y + blob.DynY),
                        Vector2.UnitX,
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
                            DisplayIcon, 
                            DisplayName, 
                            IdleIcon, 
                            Location, 
                            LocationIcon 
                        }.Where(item => !string.IsNullOrEmpty(item)));
                }
            }

            public string DisplayName = "";
            public string DisplayIcon = "";
            public string Location = "";
            public string LocationIcon = "";
            public string IdleIcon => Idle ? ":celestenet_idle:" : "";
            public bool Idle;
            public Color Color = Color.White;
            public float ScaleFactor = 0f;
            public float DynY;
            public float DynScale;
        }

    }
}
