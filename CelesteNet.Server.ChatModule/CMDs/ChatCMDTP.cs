using Celeste.Mod.CelesteNet.DataTypes;
using Microsoft.Xna.Framework;
using MonoMod.Utils;
using System;
using System.Collections.Generic;

namespace Celeste.Mod.CelesteNet.Server.Chat {
    public class ChatCMDTP : ChatCMD {

        public override string Args => "<player>";

        public override CompletionType Completion => CompletionType.Player;

        public override string Info => "Teleport to another player.";

        public override void Run(ChatCMDEnv env, List<ChatCMDArg> args) {
            CelesteNetPlayerSession? self = env.Session;
            if (self == null || env.Player == null)
                throw new Exception("Are you trying to TP as the server?");

            if (args.Count == 0)
                throw new Exception("No username.");

            if (args.Count > 1)
                throw new Exception("Invalid username or ID.");

            CelesteNetPlayerSession? other = args[0].Session;
            DataPlayerInfo otherPlayer = other?.PlayerInfo ?? throw new Exception("Invalid username or ID.");

            if (!env.Server.UserData.Load<TPSettings>(other.UID).Enabled)
                throw new Exception($"{otherPlayer.DisplayName} has blocked teleports.");

            if (self.Channel != other.Channel)
                throw new Exception($"{otherPlayer.DisplayName} is in a different channel.");

            if (!env.Server.Data.TryGetBoundRef(otherPlayer, out DataPlayerState? otherState) ||
                otherState == null ||
                otherState.SID.IsNullOrEmpty())
                throw new Exception($"{otherPlayer.DisplayName} isn't in-game.");

            DataChat? msg = env.Send($"Teleporting to {otherPlayer.DisplayName}");

            self.Request<DataSession>(400,
                (con, session) => self.WaitFor<DataPlayerFrame>(400,
                    (con, frame) => SaveAndTeleport(env, msg, self, other, otherPlayer, otherState, session, frame.Position),
                    () => SaveAndTeleport(env, msg, self, other, otherPlayer, otherState, session, null)
                ),
                () => SaveAndTeleport(env, msg, self, other, otherPlayer, otherState, null, null)
            );
        }

        private bool SaveAndTeleport(ChatCMDEnv env, DataChat? msg, CelesteNetPlayerSession self, CelesteNetPlayerSession other, DataPlayerInfo otherPlayer, DataPlayerState otherState, DataSession? savedSession, Vector2? savedPos) {
            new DynamicData(self).Set("tpHistory", new TPHistoryEntry {
                State = env.State,
                Session = savedSession,
                Position = savedPos
            });

            other.Request<DataSession>(400,
                (con, session) => other.WaitFor<DataPlayerFrame>(300,
                    (con, frame) => Teleport(env, msg, self, other, otherPlayer, otherState, session, frame.Position),
                    () => Teleport(env, msg, self, other, otherPlayer, otherState, session, null)
                ),
                () => Teleport(env, msg, self, other, otherPlayer, otherState, null, null)
            );
            return true;
        }

        private bool Teleport(ChatCMDEnv env, DataChat? msg, CelesteNetPlayerSession self, CelesteNetPlayerSession other, DataPlayerInfo otherPlayer, DataPlayerState otherState, DataSession? tpSession, Vector2? tpPos) {
            if (msg != null) {
                self.WaitFor<DataPlayerState>(6000, (con, state) => {
                    if (state.SID != otherState.SID ||
                        state.Mode != otherState.Mode ||
                        state.Level != otherState.Level)
                        return false;

                    msg.Text = $"Teleported to {otherPlayer.DisplayName}";
                    Chat.ForceSend(msg);
                    return true;

                }, () => {
                    msg.Text = $"Couldn't teleport to {otherPlayer.DisplayName} - maybe missing map?";
                    Chat.ForceSend(msg);

                    other.Request<DataMapModInfo>(1000, (con, info) => {
                        if (!string.IsNullOrEmpty(info.ModID))
                            self.Con.Send(new DataModRec {
                                ModID = info.ModID,
                                ModName = info.ModName,
                                ModVersion = info.ModVersion
                            });
                    });
                });
            }

            self.Con.Send(new DataMoveTo {
                SID = otherState.SID,
                Mode = otherState.Mode,
                Level = otherState.Level,
                Session = tpSession,
                Position = tpPos
            });

            return true;
        }

    }

    public class ChatCMDTPOn : ChatCMD {

        public override string Args => "";

        public override string Info => "Allow others to teleport to you.";

        public override void ParseAndRun(ChatCMDEnv env) {
            if (env.Session == null)
                return;

            if (env.Server.UserData.GetKey(env.Session.UID).IsNullOrEmpty())
                throw new Exception("You must be registered to enable / disable teleports!");

            env.Server.UserData.Save(env.Session.UID, new TPSettings {
                Enabled = true
            });
            env.Send("Others can teleport to you now.");
        }

    }

    public class ChatCMDTPOff : ChatCMD {

        public override string Args => "";

        public override string Info => "Prevent others from teleporting to you.";

        public override void ParseAndRun(ChatCMDEnv env) {
            if (env.Session == null)
                return;

            if (env.Server.UserData.GetKey(env.Session.UID).IsNullOrEmpty())
                throw new Exception("You must be registered to enable / disable teleports!");

            env.Server.UserData.Save(env.Session.UID, new TPSettings {
                Enabled = false
            });
            env.Send("Others can't teleport to you anymore.");
        }

    }

    public class TPSettings {
        public bool Enabled { get; set; } = true;
    }

    public class TPHistoryEntry {
        public DataPlayerState? State;
        public DataSession? Session;
        public Vector2? Position;
    }
}
