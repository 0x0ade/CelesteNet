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
    public class WSCMDKickWarn : WSCMD {
        public override bool Auth => true;
        public override object? Run(dynamic? input) {
            uint id = (uint) input?.ID;
            string? reason = (string?) input?.Reason ?? "";

            lock (Frontend.Server.Connections)
                if (Frontend.Server.PlayersByID.TryGetValue(id, out CelesteNetPlayerSession? player)) {
                    string uid = player.UID;

                    ChatModule chat = Frontend.Server.Get<ChatModule>();
                    new DynamicData(player).Set("leaveReason", chat.Settings.MessageKick);
                    player.Dispose();
                    player.Con.Send(new DataDisconnectReason { Text = string.IsNullOrEmpty(reason) ? "Kicked" : $"Kicked: {reason}" });
                    player.Con.Send(new DataInternalDisconnect());

                    UserData userData = Frontend.Server.UserData;
                    if (!reason.IsNullOrEmpty() && !userData.GetKey(uid).IsNullOrEmpty()) {
                        KickHistory kicks = userData.Load<KickHistory>(uid);
                        kicks.Log.Add(new KickHistory.Entry {
                            Reason = reason,
                            From = DateTime.UtcNow
                        });
                        userData.Save(uid, kicks);
                        Frontend.BroadcastCMD(true, "update", "/userinfos");
                    }

                    return null;
                }

            return null;
        }
    }
}
