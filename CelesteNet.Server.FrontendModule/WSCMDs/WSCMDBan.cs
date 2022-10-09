﻿using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDBan : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            JArray? uidsRaw = (JArray?) input?.UIDs;
            string[]? uids = uidsRaw?.Select(t => t.ToString()).ToArray();
            string? reason = (string?) input?.Reason;
            if (uids == null || uids.Length == 0 ||
                (reason = reason?.Trim() ?? "").IsNullOrEmpty())
                return null;

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

            foreach (string uid in uids)
                Frontend.Server.UserData.Save(uid, ban);
            Frontend.BroadcastCMD(true, "update", Frontend.Settings.APIPrefix + "/userinfos");

            return null;
        }
    }
}
