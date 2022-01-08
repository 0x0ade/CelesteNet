using Celeste.Mod.CelesteNet.DataTypes;
using Mono.Options;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetPlayerSession : IDisposable {

        public static readonly HashSet<char> IllegalNameChars = new() { ':', '#', '|' };

        public readonly CelesteNetServer Server;
        public readonly CelesteNetConnection Con;
        public readonly uint SessionID;

        private int _Alive;
        public bool Alive => Volatile.Read(ref _Alive) > 0;
        public readonly string UID, Name;

        private readonly RWLock StateLock = new();
        private readonly Dictionary<object, Dictionary<Type, object>> StateContexts = new();

        public DataPlayerInfo? PlayerInfo => Server.Data.TryGetRef(SessionID, out DataPlayerInfo? value) ? value : null;

        public Channel Channel;

        public DataInternalBlob[] AvatarFragments = Dummy<DataInternalBlob>.EmptyArray;

        private readonly object RequestNextIDLock = new();
        private uint RequestNextID = 0;

        internal CelesteNetPlayerSession(CelesteNetServer server, CelesteNetConnection con, uint sesId, string uid, string name) {
            Server = server;
            Con = con;
            SessionID = sesId;

            _Alive = 1;
            UID = uid;
            Name = name;

            Channel = server.Channels.Default;

            Interlocked.Increment(ref Server.PlayerCounter);
            Con.OnSendFilter += ConSendFilter;
            Server.Data.RegisterHandlersIn(this);
        }

        public T? Get<T>(object ctx) where T : class {
            using (StateLock.R()) {
                if (!StateContexts.TryGetValue(ctx, out Dictionary<Type, object>? states))
                    return null;

                if (!states.TryGetValue(typeof(T), out object? state))
                    return null;

                return (T) state;
            }
        }

        [return: NotNullIfNotNull("state")]
        public T? Set<T>(object ctx, T? state) where T : class {
            if (state == null)
                return Remove<T>(ctx);

            using (StateLock.W()) {
                if (!StateContexts.TryGetValue(ctx, out Dictionary<Type, object>? states))
                    StateContexts[ctx] = states = new();

                states[typeof(T)] = state;
                return state;
            }
        }

        public T? Remove<T>(object ctx) where T : class {
            using (StateLock.W()) {
                if (!StateContexts.TryGetValue(ctx, out Dictionary<Type, object>? states))
                    return null;

                if (!states.TryGetValue(typeof(T), out object? state))
                    return null;

                states.Remove(typeof(T));
                if (states.Count == 0)
                    StateContexts.Remove(ctx);
                return (T) state;
            }
        }

        internal void Start() {
            if (!string.IsNullOrEmpty(Server.Settings.MessageDiscontinue)) {
                Con.Send(new DataDisconnectReason { Text = Server.Settings.MessageDiscontinue });
                Con.Send(new DataInternalDisconnect());
                return;
            }

            Logger.Log(LogLevel.INF, "playersession", $"Startup #{SessionID} {Con} (Session UID: {UID}; Connection UID: {Con.UID})");

            // Resolver player name conflicts
            string nameSpace = Name;
            string fullNameSpace = nameSpace;
            string fullName = Name.Replace(" ", "");

            using (Server.ConLock.R()) {
                int i = 1;
                while (true) {
                    bool conflict = false;
                    foreach (CelesteNetPlayerSession other in Server.Sessions)
                        if (conflict = other.PlayerInfo?.FullName == fullName)
                            break;
                    if (!conflict)
                        break;
                    i++;
                    fullNameSpace = $"{nameSpace}#{i}";
                    fullName = $"{Name}#{i}";
                }
            }

            // Handle avatars
            string displayName = fullNameSpace;

            using (Stream? avatarStream = Server.UserData.ReadFile(UID, "avatar.png")) {
                if (avatarStream != null) {
                    string avatarId = $"celestenet_avatar_{SessionID}_";
                    displayName = $":{avatarId}: {fullNameSpace}";

                    // Split the avatar into fragments
                    List<DataNetEmoji> avatarFrags = new();
                    byte[] buf = new byte[Server.Settings.MaxPacketSize / 2];
                    int fragSize, seqNum = 0;
                    while ((fragSize = avatarStream.Read(buf, 0, buf.Length)) > 0) {
                        byte[] frag = new byte[fragSize];
                        Buffer.BlockCopy(buf, 0, frag, 0, fragSize);
                        if (avatarFrags.Count > 0)
                            avatarFrags[avatarFrags.Count - 1].MoreFragments = true;
                        avatarFrags.Add(new DataNetEmoji {
                            ID = avatarId,
                            Data = frag,
                            SequenceNumber = seqNum++,
                            MoreFragments = false
                        });
                    }

                    // Turn avatar fragments into blobs
                    AvatarFragments = avatarFrags.Select(frag => DataInternalBlob.For(Server.Data, frag)).ToArray();
                } else
                    AvatarFragments = Dummy<DataInternalBlob>.EmptyArray;
            }

            // Create the player's PlayerInfo
            DataPlayerInfo playerInfo = new() {
                ID = SessionID,
                Name = Name,
                FullName = fullName,
                DisplayName = displayName
            };
            playerInfo.Meta = playerInfo.GenerateMeta(Server.Data);
            Server.Data.SetRef(playerInfo);

            Logger.Log(LogLevel.INF, "playersession", $"Session #{SessionID} PlayerInfo: {playerInfo}");

            // Send packets to players
            DataInternalBlob blobPlayerInfo = DataInternalBlob.For(Server.Data, playerInfo);

            Con.Send(playerInfo);
            foreach (DataInternalBlob fragBlob in AvatarFragments)
                Con.Send(fragBlob);

            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession other in Server.Sessions) {
                    if (other == this)
                        continue;

                    DataPlayerInfo? otherInfo = other.PlayerInfo;
                    if (otherInfo == null)
                        continue;

                    other.Con.Send(blobPlayerInfo);
                    foreach (DataInternalBlob fragBlob in AvatarFragments)
                        other.Con.Send(fragBlob);

                    Con.Send(otherInfo);
                    foreach (DataInternalBlob fragBlob in other.AvatarFragments)
                        Con.Send(fragBlob);

                    foreach (DataType bound in Server.Data.GetBoundRefs(otherInfo))
                        if (!bound.Is<MetaPlayerPrivateState>(Server.Data) || other.Channel.ID == 0)
                            Con.Send(bound);
                }

            ResendPlayerStates();

            Con.Send(new DataReady());
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
            using (req.UpdateMeta(Server.Data)) {
                if (!req.TryGet(Server.Data, out MetaRequest? mreq))
                    mreq = new();
                lock (RequestNextIDLock)
                    mreq.ID = RequestNextID++;
                req.Set(Server.Data, mreq);
            }

            Action cancel = WaitFor<T>(timeout, (con, data) => {
                if (req.TryGet(Server.Data, out MetaRequest? mreq) &&
                    data.TryGet(Server.Data, out MetaRequestResponse? mres) &&
                    mreq.ID != mres.ID)
                    return false;

                cb(con, data);
                return true;
            }, cbTimeout);

            Con.Send(req);
            return cancel;
        }

        public void ResendPlayerStates() {
            Channel channel = Channel;

            ILookup<bool, DataInternalBlob> boundAll = Server.Data.GetBoundRefs(PlayerInfo)
                .Select(bound => new DataInternalBlob(Server.Data, bound))
                .ToLookup(blob => blob.Data.Is<MetaPlayerPrivateState>(Server.Data));
            IEnumerable<DataInternalBlob> boundAllPublic = boundAll[false];
            IEnumerable<DataInternalBlob> boundAllPrivate = boundAll[true];

            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession other in Server.Sessions) {
                    if (other == this)
                        continue;

                    foreach (DataType bound in boundAllPublic)
                        other.Con.Send(bound);
                    foreach (DataType bound in boundAllPrivate)
                        if (channel == other.Channel)
                            other.Con.Send(bound);

                    DataPlayerInfo? otherInfo = other.PlayerInfo;
                    if (otherInfo == null)
                        continue;

                    foreach (DataType bound in Server.Data.GetBoundRefs(otherInfo))
                        if (!bound.Is<MetaPlayerPrivateState>(Server.Data) || channel == other.Channel)
                            Con.Send(bound);
                }
        }

        public bool IsSameArea(CelesteNetPlayerSession other)
            => Server.Data.TryGetBoundRef(PlayerInfo, out DataPlayerState? state) && state != null && IsSameArea(Channel, state, other);

        public bool IsSameArea(Channel channel, DataPlayerState? state, CelesteNetPlayerSession other)
            =>  state != null &&
                other.Channel == channel &&
                Server.Data.TryGetBoundRef(other.PlayerInfo, out DataPlayerState? otherState) &&
                otherState != null &&
                otherState.SID == state.SID &&
                otherState.Mode == state.Mode;

        public bool ConSendFilter(CelesteNetConnection con, DataType data) {
            if (Server.Data.TryGetBoundRef(PlayerInfo, out DataNetFilterList? list) && list != null) {
                string source = data.GetSource(Server.Data);
                return string.IsNullOrEmpty(source) || list.Contains(source);
            }

            return true;
        }

        public event Action<CelesteNetPlayerSession, DataPlayerInfo?>? OnEnd;

        public void Dispose() {
            if (Interlocked.Exchange(ref _Alive, 0) <= 0)
                return;

            Logger.Log(LogLevel.INF, "playersession", $"Shutdown #{SessionID} {Con}");

            DataPlayerInfo? playerInfoLast = PlayerInfo;

            if (playerInfoLast != null)
                Server.BroadcastAsync(new DataPlayerInfo {
                    ID = SessionID
                });

            Con.OnSendFilter -= ConSendFilter;
            Server.Data.UnregisterHandlersIn(this);

            Logger.Log(LogLevel.VVV, "playersession", $"Loopend send #{SessionID} {Con}");
            Con.Send(new DataInternalLoopend(() => {
                Logger.Log(LogLevel.VVV, "playersession", $"Loopend run #{SessionID} {Con}");

                Server.Data.FreeRef<DataPlayerInfo>(SessionID);
                Server.Data.FreeOrder<DataPlayerFrame>(SessionID);

                OnEnd?.Invoke(this, playerInfoLast);
            }));

            StateLock.Dispose();
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

            bool fixup = false;
            DataPlayerInfo? player = null;

            if (data.TryGet(Server.Data, out MetaPlayerUpdate? update)) {
                update.Player = player ??= PlayerInfo;
                fixup = true;
            }

            if (data.TryGet(Server.Data, out MetaPlayerPrivateState? state)) {
                state.Player = player ??= PlayerInfo;
                fixup = true;
            }

            if (data.TryGet(Server.Data, out MetaPlayerPublicState? statePub)) {
                statePub.Player = player ??= PlayerInfo;
                fixup = true;
            }

            if (data.TryGet(Server.Data, out MetaBoundRef? boundRef) && boundRef.TypeBoundTo == DataPlayerInfo.DataID) {
                boundRef.ID = (player ?? PlayerInfo)?.ID ?? uint.MaxValue;
                fixup = true;
            }

            if (fixup)
                data.FixupMeta(Server.Data);

            return true;
        }

        public bool Filter(CelesteNetConnection con, DataPlayerFrame frame) {
            if (frame.Followers.Length > Server.Settings.MaxFollowers)
                Array.Resize(ref frame.Followers, Server.Settings.MaxFollowers);

            return true;
        }

        public bool Filter(CelesteNetConnection con, DataPlayerGraphics graphics) {
            if (graphics.HairCount > Server.Settings.MaxHairLength)
                graphics.HairCount = Server.Settings.MaxHairLength;
            return true;
        }

        public void Handle(CelesteNetConnection con, DataPlayerInfo updated) {
            if (con != Con)
                return;

            DataInternalBlob blob = new(Server.Data, updated);

            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession other in Server.Sessions) {
                    if (other == this)
                        continue;

                    other.Con.Send(blob);
                }
        }

        public void Handle(CelesteNetConnection con, DataType data) {
            if (con != Con)
                return;

            if (!Server.Data.TryGetBoundRef(PlayerInfo, out DataPlayerState? state))
                state = null;

            bool isPrivate = data.Is<MetaPlayerPrivateState>(Server.Data);
            bool isUpdate = data.Is<MetaPlayerUpdate>(Server.Data);
            if (data.Is<MetaPlayerPublicState>(Server.Data) ||
                isPrivate ||
                isUpdate) {
                Channel channel = Channel;

                DataInternalBlob blob = new(Server.Data, data);

                HashSet<CelesteNetPlayerSession> others = isPrivate || isUpdate ? channel.Players : Server.Sessions;
                using (isPrivate || isUpdate ? channel.Lock.R() :  Server.ConLock.R())
                    foreach (CelesteNetPlayerSession other in others) {
                        if (other == this)
                            continue;

                        /*
                        if (data.Is<MetaPlayerPrivateState>(Server.Data) && channel != other.Channel)
                            continue;
                        */

                        if (isUpdate && !IsSameArea(channel, state, other))
                            continue;

                        other.Con.Send(blob);
                    }
            }
        }

        #endregion

    }
}
