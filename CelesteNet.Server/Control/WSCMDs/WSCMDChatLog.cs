using Celeste.Mod.CelesteNet.DataTypes;
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
    public class WSCMDChatLog : WSCMD<int> {
        public override bool Auth => true;
        public override object Run(int count) {
            if (count <= 0)
                count = 20;
            ChatServer chat = WS.Frontend.Server.Chat;
            lock (chat.ChatLog) {
                RingBuffer<DataChat> buffer = chat.ChatBuffer;
                List<object> log = new List<object>();
                for (int i = Math.Max(-chat.ChatBuffer.Moved, -count); i < 0; i++) {
                    DataChat msg = buffer[i];
                    log.Add(msg.ToFrontendChat());
                }
                return log;
            }
        }
    }
}
