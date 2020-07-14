using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatCMDBack : ChatCMD {

        public override string Args => "";

        public override string Info => "Teleport to where you were before your last teleport.";

        public override void ParseAndRun(ChatCMDEnv env) {
            CelesteNetPlayerSession? self = env.Session;
            if (self == null || env.Player == null)
                throw new Exception("Are you trying to TP as the server?");

            DynamicData selfData = new DynamicData(self);
            TPHistoryEntry? back = selfData.Get<TPHistoryEntry>("tpHistory");
            if (back?.State == null || back?.Session == null)
                throw new Exception("Got nowhere to teleport back to.");
            selfData.Set("tpHistory", null);

            DataChat? msg = env.Send("Teleporting back");

            self.Con.Send(new DataMoveTo {
                SID = back.State.SID,
                Mode = back.State.Mode,
                Level = back.State.Level,
                Session = back.Session,
                Position = back.Position
            });

            if (msg != null) {
                msg.Text = "Teleported back";
                Chat.ForceSend(msg);
            }
        }

    }
}
