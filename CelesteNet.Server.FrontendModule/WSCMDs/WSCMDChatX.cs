using System;
using System.Linq;
using Celeste.Mod.CelesteNet.DataTypes;
using Celeste.Mod.CelesteNet.Server.Chat;
using Newtonsoft.Json.Linq;

namespace Celeste.Mod.CelesteNet.Server.Control {
    public class WSCMDChatX : WSCMD {
        public override bool MustAuth => true;
        public override object? Run(dynamic? input) {
            if (input == null)
                return null;

            string? text = input.Text;
            string? tag = input.Tag;
            string? color = input.Color;
            JArray? targets = input.Targets;

            ChatModule chat = Frontend.Server.Get<ChatModule>();
            DataChat msg = new()
            {
                Text = text ?? "",
                Tag = tag ?? "",
                Color = chat.Settings.ColorBroadcast
            };

            if (!string.IsNullOrEmpty(color))
                msg.Color = ColorHelpers.HexToColor(color!);

            if (targets != null && targets.Count > 0)
            {
                uint[] targetIDs = targets
                    .Where(el => el.Type == JTokenType.Integer)
                    .Select(el => {
                        try {
                            return el.Value<uint>();
                        } catch (OverflowException) {
                            return 0u;
                        }
                    })
                    .Where(id => id != 0u)
                    .ToArray();

                DataPlayerInfo[] targetSessions = Frontend.Server.Sessions
                    .Where(s => targetIDs.Contains(s.SessionID) && s.PlayerInfo != null)
                    .Select(s => s.PlayerInfo!)
                    .ToArray();

                if (targetSessions.Length == 0)
                    // the message would reach 0 players
                    return null;

                msg.Targets = targetSessions;
            }

            chat.Broadcast(msg);
            return msg.ToDetailedFrontendChat();
        }
    }
}
