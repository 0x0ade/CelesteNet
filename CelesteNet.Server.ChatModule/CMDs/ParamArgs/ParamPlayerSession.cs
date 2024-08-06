using Celeste.Mod.CelesteNet.DataTypes;
using System;
using System.Linq;

namespace Celeste.Mod.CelesteNet.Server.Chat.Cmd {
    public class ParamPlayerSession : Param {
        public override string Help => $"An {(IngameOnly ? "in-game" : "online")} player";
        public override string PlaceholderName => "player";
        protected override string ExampleValue => "Madeline";
        public bool IngameOnly = false;

        public ParamPlayerSession(ChatModule chat, Action<string, CmdEnv, ICmdArg>? validate = null, ParamFlags flags = ParamFlags.None,
                                    bool ingameOnly = false) : base(chat, validate, flags) {
            IngameOnly = ingameOnly;
        }

        public override bool TryParse(string raw, CmdEnv env, out ICmdArg? arg) {
            arg = null;
            CelesteNetPlayerSession? session = null;
            uint playerID;

            // doing things in this order so that
            // 1. check if string is '#' plus a number - check if it's a player ID
            // 2. check if there's a player with this name, regardless if it's a number
            // 3. only after that do another attempt at interpreting the number as a player ID
            // ... this is of course for the very real scenario of having players "123", "#123" and a player with ID "123" all online, and we can address them all.
            if (raw.StartsWith('#') && uint.TryParse(raw.Substring(1), out playerID)) {
                Chat.Server.PlayersByID.TryGetValue(playerID, out session);
            } else if (!string.IsNullOrWhiteSpace(raw)) {
                using (Chat.Server.ConLock.R()) {
                    session = 
                        // check for exact name
                        env.Server.Sessions.FirstOrDefault(session => session.PlayerInfo?.FullName.Equals(raw, StringComparison.OrdinalIgnoreCase) ?? false) ??
                        // check for partial name in channel
                        env.Session?.Channel.Players.FirstOrDefault(session => session.PlayerInfo?.FullName.StartsWith(raw, StringComparison.OrdinalIgnoreCase) ?? false) ??
                        // check for partial name elsewhere
                        env.Server.Sessions.FirstOrDefault(session => session.PlayerInfo?.FullName.StartsWith(raw, StringComparison.OrdinalIgnoreCase) ?? false);
                }
            }
            if (session == null && uint.TryParse(raw, out playerID)) {
                Chat.Server.PlayersByID.TryGetValue(playerID, out session);
            }

            if (session != null) {
                arg = new CmdArgPlayerSession(session);

                if (IngameOnly) {
                    if (!Chat.Server.Data.TryGetBoundRef(session.PlayerInfo, out DataPlayerState? otherState) ||
                        otherState == null || otherState.SID.IsNullOrEmpty()) {
                        throw new ParamException("Player is not in-game.");
                    }
                }

                Validate?.Invoke(raw, env, arg);
                return true;
            }

            throw new ParamException($"Player '{raw}' not found.");
        }

    }

    public class CmdArgPlayerSession : ICmdArg {

        public CelesteNetPlayerSession? Session { get; }

        public CmdArgPlayerSession(CelesteNetPlayerSession? val) {
            Session = val;
        }

        public override string ToString() => Session?.ToString() ?? "";
    }
}
