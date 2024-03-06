using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using MonoMod.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace Celeste.Mod.CelesteNet.Server.Control
{
    public class WSCMDBan : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            JArray? uidsRaw = (JArray?) input?.UIDs;
            string[]? uids = uidsRaw?.Select(t => t.ToString()).ToArray();
            string? reason = (string?) input?.Reason;

            Logger.Log(LogLevel.VVV, "frontend", $"Ban called:\n{uids}\nReason: {reason}");

            if (uids == null || uids.Length == 0 ||
                (reason = reason?.Trim() ?? "").IsNullOrEmpty())
                return false;

            // for Guests their UID will look like =>   fb-con-tcpudp---ffff-127.0.0.1
            // and their connection UID will look like =>  con-tcpudp---ffff-127.0.0.1

            // so if we've got a (player-) UID that starts with "fb-"...
            string? guestFB = uids.FirstOrDefault(uid => uid.StartsWith("fb-"));

            // ...see if the "non-FB" version of the UID (i.e. the Con.UID) is in the list,
            // don't use fb-UID since it's pointless clutter to ban both. Con.UID ban is enough.
            if (guestFB != null && uids.Contains(guestFB.Substring(3))) {
                uids = uids.Where(uid => uid != guestFB).ToArray();
            }

            BanInfo ban = new() {
                UID = uids[0],
                Reason = reason,
                From = DateTime.UtcNow
            };

            CelesteNetPlayerSession[] players;
            using (Frontend.Server.ConLock.R())
                players = Frontend.Server.Sessions.ToArray();

            foreach (string uid in uids) {
                foreach (CelesteNetPlayerSession player in players) {
                    if (player.UID != uid && player.Con.UID != uid)
                        continue;

                    if (ban.Name.IsNullOrEmpty())
                        ban.Name = player.PlayerInfo?.FullName ?? "";

                    ChatModule chat = Frontend.Server.Get<ChatModule>();
                    new DynamicData(player).Set("leaveReason", chat.Settings.MessageBan);
                    player.Dispose();
                    player.Con.Send(new DataDisconnectReason { Text = "Banned: " + reason });
                    player.Con.Send(new DataInternalDisconnect());
                }
            }

            foreach (string uid in uids) {
                Frontend.Server.UserData.Save(uid, ban);
                Logger.Log(LogLevel.VVV, "frontend", $"Saved ban to UserData:\n{uid}");
            }
            Frontend.BroadcastCMD(true, "update", Frontend.Settings.APIPrefix + "/userinfos");

            return true;
        }
    }
}
