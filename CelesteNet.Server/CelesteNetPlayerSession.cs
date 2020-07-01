using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Options;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetPlayerSession : IDisposable {

        public static readonly char[] IllegalNameChars = new char[] { ':', '#', '|' };

        public readonly CelesteNetServer Server;
        public readonly CelesteNetConnection Con;
        public readonly uint ID;
        public readonly string ConUID;
        public string UID;

        public DataPlayerInfo? PlayerInfo => Server.Data.TryGetRef(ID, out DataPlayerInfo? value) ? value : null;
        public Channel Channel => Server.Channels.Get(this);

        public DataNetEmoji? AvatarEmoji;

        public CelesteNetPlayerSession(CelesteNetServer server, CelesteNetConnection con, uint id) {
            Server = server;
            Con = con;
            ID = id;

            ConUID = UID = $"con-{con.UID}";

            Server.Data.RegisterHandlersIn(this);
        }

        public void Start<T>(DataHandshakeClient<T> handshake) where T : DataHandshakeClient<T> {
            Logger.Log(LogLevel.INF, "playersession", $"Startup #{ID} {Con}");
            lock (Server.Connections) {
                Server.PlayersByCon[Con] = this;
                Server.PlayersByID[ID] = this;
            }

            if (Server.UserData.TryLoad(UID, out BanInfo ban) && !string.IsNullOrEmpty(ban.Reason)) {
                Con.Send(new DataDisconnectReason { Text = $"IP banned: {ban.Reason}" });
                Con.Send(new DataInternalDisconnect());
                return;
            }

            string name = handshake.Name;
            if (name.StartsWith("#")) {
                string uid = Server.UserData.GetUID(name.Substring(1));
                if (string.IsNullOrEmpty(uid)) {
                    Con.Send(new DataDisconnectReason { Text = "Invalid user key" });
                    Con.Send(new DataInternalDisconnect());
                    return;
                }
                UID = uid;

                if (!Server.UserData.TryLoad(uid, out BasicUserInfo userinfo)) {
                    Con.Send(new DataDisconnectReason { Text = "User info missing" });
                    Con.Send(new DataInternalDisconnect());
                    return;
                }

                name = userinfo.Name.Sanitize(IllegalNameChars, true);
                if (name.Length > Server.Settings.MaxNameLength)
                    name = name.Substring(0, Server.Settings.MaxNameLength);
                if (string.IsNullOrEmpty(name))
                    name = "Ghost";

                if (Server.UserData.TryLoad(UID, out ban) && !string.IsNullOrEmpty(ban.Reason)) {
                    Con.Send(new DataDisconnectReason { Text = $"{name} banned: {ban.Reason}" });
                    Con.Send(new DataInternalDisconnect());
                    return;
                }

            } else {
                name = name.Sanitize(IllegalNameChars);
                if (name.Length > Server.Settings.MaxGuestNameLength)
                    name = name.Substring(0, Server.Settings.MaxGuestNameLength);
                if (string.IsNullOrEmpty(name))
                    name = "Guest";
            }

            if (name.Length > Server.Settings.MaxNameLength)
                name = name.Substring(0, Server.Settings.MaxNameLength);

            string fullName = name;

            lock (Server.Connections)
                for (int i = 2; Server.PlayersByCon.Values.Any(other => other.PlayerInfo?.FullName == fullName); i++)
                    fullName = $"{name}#{i}";

            string displayName = fullName;

            using (Stream? avatar = Server.UserData.ReadFile(UID, "avatar.png")) {
                if (avatar != null) {
                    AvatarEmoji = new DataNetEmoji {
                        ID = $"celestenet_avatar_{fullName}_",
                        Data = avatar.ToBytes()
                    };
                    displayName = $":{AvatarEmoji.ID}: {fullName}";
                }
            }

            DataPlayerInfo playerInfo = new DataPlayerInfo {
                ID = ID,
                Name = name,
                FullName = fullName,
                DisplayName = displayName
            };
            Server.Data.SetRef(playerInfo);

            Logger.Log(LogLevel.INF, "playersession", playerInfo.ToString());

            Con.Send(new DataHandshakeServer {
                PlayerInfo = playerInfo
            });
            Con.Send(AvatarEmoji);

            lock (Server.Connections) {
                foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                    if (other == this)
                        continue;

                    other.Con.Send(playerInfo);
                    other.Con.Send(AvatarEmoji);

                    DataPlayerInfo? otherInfo = other.PlayerInfo;
                    if (otherInfo == null)
                        continue;

                    Con.Send(otherInfo);
                    Con.Send(other.AvatarEmoji);

                    foreach (DataType bound in Server.Data.GetBoundRefs(otherInfo))
                        if (!(bound is IDataPlayerState) || other.Channel.ID == 0)
                            Con.Send(bound);
                }
            }

            ResendPlayerStates();

            foreach (DataType data in Server.Data.GetAllStatic())
                Con.Send(data);

            Server.InvokeOnSessionStart(this);
        }

        public Action WaitFor<T>(DataFilter<T> cb) where T : DataType<T>
            => WaitFor(0, cb, null);

        public Action WaitFor<T>(int timeout, DataFilter<T> cb, Action? cbTimeout = null) where T : DataType<T>
            => Server.Data.WaitFor<T>(timeout, (con, data) => {
                if (Con != con)
                    return false;
                return cb(con, data);
            }, cbTimeout);

        public Action Request<T>(DataHandler<T> cb) where T : DataType<T>, IDataRequestable
            => Request(0, Activator.CreateInstance(typeof(T).GetRequestType()) as DataType ?? throw new Exception($"Invalid requested type: {typeof(T).FullName}"), cb, null);

        public Action Request<T>(int timeout, DataHandler<T> cb, Action? cbTimeout = null) where T : DataType<T>, IDataRequestable
            => Request(timeout, Activator.CreateInstance(typeof(T).GetRequestType()) as DataType ?? throw new Exception($"Invalid requested type: {typeof(T).FullName}"), cb, cbTimeout);

        public Action Request<T>(DataType req, DataHandler<T> cb) where T : DataType<T>, IDataRequestable
            => Request(0, req, cb, null);

        public Action Request<T>(int timeout, DataType req, DataHandler<T> cb, Action? cbTimeout = null) where T : DataType<T>, IDataRequestable {
            Action cancel = WaitFor<T>(timeout, (con, data) => {
                cb(con, data);
                return true;
            }, cbTimeout);
            Con.Send(req);
            return cancel;
        }

        public void ResendPlayerStates() {
            Channel channel = Channel;

            lock (Server.Connections) {
                foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                    if (other == this)
                        continue;

                    foreach (DataType bound in Server.Data.GetBoundRefs(PlayerInfo))
                        if (!(bound is IDataPlayerState) || channel == other.Channel)
                            other.Con.Send(bound);

                    DataPlayerInfo? otherInfo = other.PlayerInfo;
                    if (otherInfo == null)
                        continue;

                    foreach (DataType bound in Server.Data.GetBoundRefs(otherInfo))
                        if (!(bound is IDataPlayerState) || channel == other.Channel)
                            Con.Send(bound);
                }
            }
        }

        public bool IsSameChannel(CelesteNetPlayerSession other)
            => Server.Data.TryGetBoundRef(PlayerInfo, out DataPlayerState? state) && state != null && IsSameChannel(Channel, state, other);

        public bool IsSameChannel(Channel channel, DataPlayerState? state, CelesteNetPlayerSession other)
            =>  state != null &&
                other.Channel == channel;

        public bool IsSameArea(CelesteNetPlayerSession other)
            => Server.Data.TryGetBoundRef(PlayerInfo, out DataPlayerState? state) && state != null && IsSameArea(Channel, state, other);

        public bool IsSameArea(Channel channel, DataPlayerState? state, CelesteNetPlayerSession other)
            =>  state != null &&
                other.Channel == channel &&
                Server.Data.TryGetBoundRef(other.PlayerInfo, out DataPlayerState? otherState) &&
                otherState != null &&
                otherState.SID == state.SID &&
                otherState.Mode == state.Mode;

        public event Action<CelesteNetPlayerSession, DataPlayerInfo?>? OnEnd;

        public void Dispose() {
            Logger.Log(LogLevel.INF, "playersession", $"Shutdown #{ID} {Con}");

            DataPlayerInfo? playerInfoLast = PlayerInfo;

            lock (Server.Connections) {
                Server.PlayersByCon.Remove(Con);
                Server.PlayersByID.Remove(ID);
            }

            if (playerInfoLast != null)
                Server.Broadcast(new DataPlayerInfo {
                    ID = ID
                });

            Server.Data.FreeRef<DataPlayerInfo>(ID);
            Server.Data.FreeOrder<DataPlayerFrame>(ID);

            Server.Data.UnregisterHandlersIn(this);

            OnEnd?.Invoke(this, playerInfoLast);
        }


        #region Handlers

        public bool Filter(CelesteNetConnection con, DataPlayerInfo updated) {
            // Make sure that a player can only update their own info.
            if (con != Con)
                return true;

            DataPlayerInfo? old = PlayerInfo;
            if (old == null)
                return true;

            updated.ID = old.ID;
            updated.Name = old.Name;
            updated.FullName = old.FullName;
            updated.DisplayName = old.DisplayName;

            return true;
        }

        public bool Filter(CelesteNetConnection con, DataType data) {
            if (con != Con)
                return true;

            if (data is IDataPlayerState bound)
                bound.Player = PlayerInfo;

            if (data is IDataPlayerUpdate update)
                update.Player = PlayerInfo;

            return true;
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo updated) {
            if (con != Con)
                return;

            lock (Server.Connections) {
                foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                    if (other == this)
                        continue;

                    other.Con.Send(updated);
                }
            }
        }

        public void Handle(CelesteNetConnection con, DataType data) {
            if (con != Con)
                return;

            if (!Server.Data.TryGetBoundRef(PlayerInfo, out DataPlayerState? state))
                state = null;

            if (data is IDataBoundRef<DataPlayerInfo> ||
                data is IDataPlayerState ||
                data is IDataPlayerUpdate) {
                Channel channel = Channel;

                lock (Server.Connections) {
                    foreach (CelesteNetPlayerSession other in Server.PlayersByCon.Values) {
                        if (other == this)
                            continue;

                        if (data is IDataPlayerState && channel != other.Channel)
                            continue;

                        if (data is IDataPlayerUpdate && !IsSameArea(channel, state, other))
                            continue;

                        other.Con.Send(data);
                    }
                }
            }
        }

        #endregion

    }
}
