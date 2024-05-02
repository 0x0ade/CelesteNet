using System;
using System.Linq;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using MonoMod.Utils;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDBanExt : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            uint id = (uint)input?.ID;
            string? connInfo = (string?)input?.ConnInfo;
            bool? banConnUID = (bool?)input?.BanConnUID;
            string? reason = (string?)input?.Reason;
            string? connUid = (string?)input?.ConnUID;
            bool quiet = ((bool?)input?.Quiet) ?? false;

            Logger.Log(LogLevel.VVV, "frontend", $"BanExt called:\n{connUid} => {connInfo} (ban connUID: {banConnUID}, q: {quiet})\nReason: {reason}");

            if ((connInfo = connInfo?.Trim() ?? "").IsNullOrEmpty() ||
                (reason = reason?.Trim() ?? "").IsNullOrEmpty() ||
                (connUid = connUid?.Trim() ?? "").IsNullOrEmpty())
                return false;

            // connInfo should include "key" at this point e.g. CheckMAC#Y2F0IGdvZXMgbWVvdw==
            string[] splitConnInfo = connInfo.Split(ConnFeatureUtils.kvSeparator);
            Logger.Log(LogLevel.VVV, "frontend", $"BanExt split connInfo: {splitConnInfo}");

            if (splitConnInfo.Length < 2)
                return false;

            CelesteNetPlayerSession[] players;
            CelesteNetPlayerSession? p;

            using (Frontend.Server.ConLock.R()) {
                players = Frontend.Server.Sessions.ToArray();

                Frontend.Server.PlayersByID.TryGetValue(id, out p);
            }

            string connInfoVal = splitConnInfo[1];

            // Just to make extra sure we got the right guy, I guess
            if (p != null && p.Con is ConPlusTCPUDPConnection pCon
                && pCon.ConnFeatureData.TryGetValue(splitConnInfo[0], out string? connVal)
                && !string.IsNullOrEmpty(connVal)) {
                connInfoVal = connVal;
            }

            // UID will be the connectionUID as extra info, further down the banned identifier is where it'll be stored
            BanInfo ban = new() {
                UID = connUid,
                Reason = reason,
                From = DateTime.UtcNow
            };

            foreach (CelesteNetPlayerSession player in players) {
                ConPlusTCPUDPConnection? plusCon = player.Con as ConPlusTCPUDPConnection;
                if (plusCon == null)
                    continue;

                if (!plusCon.ConnFeatureData.ContainsValue(connInfoVal))
                    continue;

                if (ban.Name.IsNullOrEmpty())
                    ban.Name = player.PlayerInfo?.FullName ?? "";

                ChatModule chat = Frontend.Server.Get<ChatModule>();
                if (!quiet)
                    new DynamicData(player).Set("leaveReason", chat.Settings.MessageBan);
                player.Con.Send(new DataDisconnectReason { Text = "Banned: " + reason });
                player.Con.Send(new DataInternalDisconnect());
                player.Dispose();
            }

            // stored with the "full" connInfo which includes the "key" e.g. CheckMAC#Y2F0IGdvZXMgbWVvdw==
            Frontend.Server.UserData.Save(connInfo, ban);

            Logger.Log(LogLevel.VVV, "frontend", $"BanExt ban saved for: {connInfo}");

            if (banConnUID ?? false) {
                // reuse banInfo but store for connection UID like a regular ban
                Frontend.Server.UserData.Save(connUid, ban);
                Logger.Log(LogLevel.VVV, "frontend", $"BanExt ban saved for: {connUid}");
            }

            Frontend.BroadcastCMD(true, "update", Frontend.Settings.APIPrefix + "/userinfos");

            return true;
        }
    }
}
