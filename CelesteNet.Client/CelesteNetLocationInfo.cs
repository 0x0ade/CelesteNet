using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet.Client {
    public class CelesteNetLocationInfo {

        public string SID { get; set; }
        public AreaData Area { get; set; }
        public string Name { get; set; }
        public string Side { get; set; }
        public string Level { get; set; }
        public string Icon { get; set; }
        public string EmoteID => string.IsNullOrWhiteSpace(SID) ? "" : $"celestenet_SID_{SID}_";

        private bool emoteLoaded = false;
        public string Emote => LoadIconEmote() ? $":{EmoteID}:" : "";
        public bool IsRandomizer => Name.StartsWith("randomizer/");

        public CelesteNetLocationInfo() { }

        public CelesteNetLocationInfo(string sid) {
            SID = sid;
            Area = AreaData.Get(SID);

            Name = Area?.Name?.DialogCleanOrNull(Dialog.Languages["english"]) ?? SID;
            Side = "A";
            Level = "";
            Icon = "";

            if (!IsRandomizer && Area != null) {
                Icon = Area.Icon;

                string lobbySID = Area?.Meta?.Parent;
                AreaData lobby = string.IsNullOrEmpty(lobbySID) ? null : AreaData.Get(lobbySID);
                if (lobby?.Icon != null)
                    Icon = lobby.Icon;
            }
        }

        public CelesteNetLocationInfo(DataPlayerState state) : this(state?.SID) {

            if (state != null) {
                Side = ((char)('A' + (int)state.Mode)).ToString();
                Level = state.Level;
            }

            if (!IsRandomizer && Area != null) {
                Icon = Area.Icon;

                string lobbySID = Area?.Meta?.Parent;
                AreaData lobby = string.IsNullOrEmpty(lobbySID) ? null : AreaData.Get(lobbySID);
                if (lobby?.Icon != null)
                    Icon = lobby.Icon;
            }
        }

        public bool LoadIconEmote() {
            if (emoteLoaded)
                return true;

            if (
                // Icon exists
                !string.IsNullOrWhiteSpace(Icon) &&
                // Can construct Emoji ID
                !string.IsNullOrWhiteSpace(EmoteID) &&
                // Icon is loaded
                GFX.Gui.Has(Icon)
            ) {
                if (!Emoji.Registered.Contains(EmoteID)) {
                    // We need to downscale the icon to fit in chat
                    // Due to our previous checks, this is never null
                    Monocle.MTexture icon = GFX.Gui[Icon];
                    float scale = 64f / icon.Height;

                    Monocle.MTexture tex = new(new(icon.Texture), icon.ClipRect) { ScaleFix = scale };
                    Emoji.Register(EmoteID, tex);
                    Emoji.Fill(CelesteNetClientFont.Font);
                }
                emoteLoaded = Emoji.Registered.Contains(EmoteID);
            }
            return emoteLoaded;
        }
    }
}
