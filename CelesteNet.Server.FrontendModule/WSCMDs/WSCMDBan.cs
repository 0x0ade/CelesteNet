using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDBan : WSCMD {
        public override bool Auth => true;
        public override object? Run(dynamic? input) {
            string? uid = (string?) input?.UID;
            string? reason = (string?) input?.Reason;
            if (string.IsNullOrEmpty(uid = uid?.Trim() ?? "") ||
                string.IsNullOrEmpty(reason = reason?.Trim() ?? ""))
                return null;

            Frontend.Server.UserData.Save(uid, new BanInfo {
                Reason = reason,
                From = DateTime.UtcNow
            });
            Frontend.BroadcastCMD(true, "update", "/userinfos");

            lock (Frontend.Server.Connections)
                foreach (CelesteNetPlayerSession player in Frontend.Server.PlayersByID.Values.ToArray()) {
                    if (player.UID != uid && player.ConUID != uid)
                        continue;

                    ChatModule chat = Frontend.Server.Get<ChatModule>();
                    new DynamicData(player).Set("leaveReason", chat.Settings.MessageBan);
                    player.Dispose();
                    player.Con.Send(new DataDisconnectReason { Text = "Banned: " + reason });
                    player.Con.Send(new DataInternalDisconnect());
                }

            return null;
        }
    }
}
