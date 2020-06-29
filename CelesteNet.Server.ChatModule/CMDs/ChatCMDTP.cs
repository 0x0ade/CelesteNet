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
    public class ChatCMDTP : ChatCMD {

        public override string Args => "<user>";

        public override string Info => "Teleport to another user.";

        public override void Run(ChatCMDEnv env, params ChatCMDArg[] args) {
            CelesteNetPlayerSession? self = env.Session;
            DataPlayerInfo? player = env.Player;
            if (self == null || player == null)
                throw new Exception("Are you trying to TP as the server?");

            if (args.Length == 0)
                throw new Exception("No username.");

            if (args.Length > 1)
                throw new Exception("Invalid username or ID.");

            CelesteNetPlayerSession? other = args[0].Session;
            DataPlayerInfo otherPlayer = other?.PlayerInfo ?? throw new Exception("Invalid username or ID.");

            if (env.Server.Channels.Get(self) != env.Server.Channels.Get(other))
                throw new Exception("Different channel.");

            if (!env.Server.Data.TryGetBoundRef(otherPlayer, out DataPlayerState? otherState) ||
                otherState == null ||
                string.IsNullOrEmpty(otherState.SID))
                throw new Exception($"{otherPlayer.DisplayName} isn't in-game.");

            DataChat? msg = env.Send($"Teleporting to {otherPlayer.DisplayName}");

            other.Request<DataSession>(300,
                (con, session) => other.WaitFor<DataPlayerFrame>(300,
                    (con, frame) => Teleport(env, msg, self, otherPlayer, otherState, session, frame.Position),
                    () => Teleport(env, msg, self, otherPlayer, otherState, session, null)
                ),
                () => Teleport(env, msg, self, otherPlayer, otherState, null, null)
            );
        }

        private void Teleport(ChatCMDEnv env, DataChat? msg, CelesteNetPlayerSession self, DataPlayerInfo otherPlayer, DataPlayerState otherState, DataSession? session, Vector2? pos) {
            self.Con.Send(new DataMoveTo {
                SID = otherState.SID,
                Mode = otherState.Mode,
                Level = otherState.Level,
                Session = session,
                Position = pos
            });

            // TODO: Verify that client replies with updated state?
            if (msg != null) {
                msg.Text = $"Teleported to {otherPlayer.DisplayName}";
                Chat.ForceSend(msg);
            }
        }

    }
}
