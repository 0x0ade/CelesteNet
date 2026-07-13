using System;
using System.Linq;
using Celeste.Mod.CelesteNet.Client.Components;
using Celeste.Mod.CelesteNet.Client.Entities;
using Celeste.Mod.CelesteNet.DataTypes;
using Monocle;
using MonoMod.ModInterop;

namespace Celeste.Mod.CelesteNet.Client
{
    public static class Interop {

        private static CelesteNetClientModule? ClientInstance => CelesteNetClientModule.Instance;
        private static CelesteNetClientContext? ClientContext => ClientInstance?.Context;

        private static CelesteNetMainComponent? MainComp => ClientContext?.Main;

        public static void Load() {
            typeof(InteropExportsClient).ModInterop();
        }

        [ModExportName("CelesteNet.Client")]
        public static class InteropExportsClient {

            public static bool? IsAlive => ClientInstance?.IsAlive;

            public static bool? IsClientReady => ClientInstance?.Client?.IsReady;

            public static bool GetInteractions()
                => CelesteNetClientModule.Session?.InteractionsOverride ?? CelesteNetClientModule.Settings?.InGame?.Interactions ?? false;

            public static bool? GetInteractionsSessionOverride()
                => CelesteNetClientModule.Session?.InteractionsOverride;

            public static void SetInteractionsSessionOverride(bool? value) {
                if (CelesteNetClientModule.Session != null)
                    CelesteNetClientModule.Session.InteractionsOverride = value;
            }

            public static bool GetAvatarsDisabled() => ClientContext?.Client?.Options?.AvatarsDisabled ?? !CelesteNetClientModule.Settings.ReceivePlayerAvatars;

            public static ulong GetSupportedClientFeatures() => (ulong) (ClientContext?.Client?.Options.SupportedClientFeatures ?? 0);
        }

        private static CelesteNetChatComponent? Chat => ClientContext?.Chat;

        [ModExportName("CelesteNet.Chat")]
        public static class InteropExportsChat {

            public static bool IsOpen() => Chat?.Active ?? false;

            public static uint[]? GetMessageIDs(bool specialOnly = false) => (specialOnly ? Chat?.LogSpecial : Chat?.Log)?.Select((DataChat msg) =>  msg.ID).ToArray();

            public static string? GetMessageText(uint id) => GetMsgByID(id)?.Text;

            public static string? GetMessageTag(uint id) => GetMsgByID(id)?.Tag;

            public static string? GetMessageColor(uint id) => GetMsgByID(id)?.Color.ToHex();

            public static DateTime? GetMessageDate(uint id) => GetMsgByID(id)?.Date;

            public static uint[]? GetMessageTargets(uint id) => GetMsgByID(id)?.Targets?.Select(playerinfo => playerinfo.ID).ToArray();

            private static DataChat? GetMsgByID(uint id) {
                if (Chat == null) return null;
                if (Chat.Log.Find(msg => msg.ID == id) is DataChat found)
                    return found;
                return null;
            }
        }

        private static CelesteNetPlayerListComponent? PlayerList => ClientContext?.Get<CelesteNetPlayerListComponent>();

        [ModExportName("CelesteNet.PlayerList")]
        public static class InteropExportsPlayerList {

            public static bool IsOpen() => PlayerList?.PropActive ?? false;

            public static void SetOpen(bool value) {
                if (PlayerList != null) PlayerList.PropActive = value;
            }
        }

        private static CelesteNetEmoteComponent? EmoteComp => ClientContext?.Get<CelesteNetEmoteComponent>();

        [ModExportName("CelesteNet.Emotes")]
        public static class InteropExportsEmotes {
            public static Entity? GetEmoteWheel() => EmoteComp?.Wheel;

            public static bool? IsEmoteWheelOpen() => EmoteComp?.Wheel?.Shown;

            public static void SendEmote(string text) => EmoteComp?.Send(text);

            public static void SendEmote(int num) => EmoteComp?.Send(num);

            public static string[]? GetEmoteList() => CelesteNetClientModule.Settings.Emotes;
        }

        [ModExportName("CelesteNet.Players")]
        public static class InteropExportsPlayers {

            public static uint[]? GetPlayerIDs() => GetPlayerInfos()?.Select(x => x.ID).ToArray();

            public static string[]? GetPlayerNames() => GetPlayerInfos()?.Select(x => x.Name).ToArray();

            public static string? GetName(uint id) => GetPlayerInfoByID(id)?.Name;

            public static string? GetFullName(uint id) => GetPlayerInfoByID(id)?.FullName;

            public static string? GetDisplayName(uint id) => GetPlayerInfoByID(id)?.DisplayName;

            private static DataPlayerInfo[]? GetPlayerInfos() => ClientContext?.Client?.Data.GetRefs<DataPlayerInfo>();

            private static DataPlayerInfo? GetPlayerInfoByID(uint id) {
                if (ClientContext?.Client?.Data is not DataContext data)
                    return null;
                return data.TryGetRef<DataPlayerInfo>(id, out var info) ? info : null;
            }
        }

        [ModExportName("CelesteNet.Ghosts")]
        public static class InteropExportsGhosts {

            public static uint[]? GetGhostIDs() => MainComp?.Ghosts.Keys.ToArray();

            public static Entity? GetGhostEntity(uint id) => GhostById(id);

            public static bool? IsGhostDead(uint id) => GhostById(id)?.Dead;

            private static Ghost? GhostById(uint id) {
                if (MainComp == null || !MainComp.Ghosts.TryGetValue(id, out Ghost? ghost))
                    return null;
                return ghost;
            }

        }

        [ModExportName("CelesteNet.Channels")]
        public static class InteropExportsChannels {

            public static uint[]? GetChannelIDs() => GetChannelList()?.List.Select(ch => ch.ID).ToArray();

            public static string[]? GetChannelNames() => GetChannelList()?.List.Select(ch => ch.Name).ToArray();

            public static uint? GetOwnChannelID() {
                if (ClientContext?.Client?.PlayerInfo is not DataPlayerInfo player)
                    return null;

                DataChannelList? list = GetChannelList();

                return list?.List.FirstOrDefault(c => c.Players.Contains(player.ID))?.ID;
            }

            public static string? GetChannelName(uint id) => GetChannelByID(id)?.Name;

            public static uint[]? GetChannelPlayerIDs(uint id) => GetChannelByID(id)?.Players;

            private static DataChannelList? GetChannelList() => PlayerList?.Channels;

            private static DataChannelList.Channel? GetChannelByID(uint id) => GetChannelList()?.List.FirstOrDefault(ch => ch.ID == id);
        }

        [ModExportName("CelesteNet.Data")]
        public static class InteropExportsData {
        }
    }
}
