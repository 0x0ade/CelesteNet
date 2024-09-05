using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using Celeste.Mod.CelesteNet.DataTypes;
using Monocle;

namespace Celeste.Mod.CelesteNet.Server {
    public class CelesteNetPlayerSession : IDisposable {

        public static readonly HashSet<char> IllegalNameChars = new() { ':', '#', '|' };

        // I added a check so that it can't pick the same word for prefix and "character",
        // and now I'm throwing some silly ones into both categories and noone shall stop me
        public static readonly string[] GuestNamePrefixes =
        {
            "Dashing",  "Jumping",  "Super", "Hyper", "Hopping",
            "Spinning", "Crouched", "Blue",  "Pink",  "Red",
            "Climbing", "Falling",  "Dream", "Awake", "Celestial",
            "Subpixel", "Dashless", "Windy", "Pride", "Bouncy",
            "Forsaken", "Neutral",  "Core",  "Space", "Mirror",
            "Golden",   "Summit",   "Moon",  "Other", "Jammy",
            "Rainbow",  "Parrot",   "Nyan",  "Jelly", "Heart",
            "Puffer",   "Celeste",  "Snip",  "Jade",  "Temple",
            "Cloud",    "Petal",    "Celery"
        };
        public static readonly string[] GuestNameCharacter =
        {
            "Madeline", "Badeline", "Maddy",  "Baddy",   "Strawberry",
            "Granny",   "Celia",    "Zipper", "Spinner", "Waterbear",
            "Oshiro",   "Kevin",    "Seeker", "Puffer",  "Berry",
            "Snowball", "Cassette", "Theo",   "Fish",    "Cloud",
            "Bubble",   "Booster",  "Jelly",  "Feather", "Bird",
            "Petal",    "Spring",   "Jump",   "Dash",    "Farewell",
            "Maddie",   "Baddie",   "Jam",    "Nyan",    "Parrot",
            "Heart",    "Rainbow",  "Orb",    "Mountain"
        };

        public readonly CelesteNetServer Server;
        public readonly CelesteNetConnection Con;
        public readonly uint SessionID;
        public readonly CelesteNetClientOptions ClientOptions;

        private int _Alive;
        public bool Alive => Volatile.Read(ref _Alive) > 0;
        public readonly string UID, Name;

        private readonly RWLock StateLock = new();
        private readonly Dictionary<object, Dictionary<Type, object>> StateContexts = new();

        public DataPlayerInfo? PlayerInfo => Server.Data.TryGetRef(SessionID, out DataPlayerInfo? value) ? value : null;

        public uint LastWhisperSessionID;

        public Channel Channel;

        public DataInternalBlob[] AvatarFragments = Dummy<DataInternalBlob>.EmptyArray;

        public HashSet<CelesteNetPlayerSession> AvatarSendQueue = new HashSet<CelesteNetPlayerSession>();

        private readonly object RequestNextIDLock = new();
        private uint RequestNextID = 0;

        private DataNetFilterList? FilterList = null;

        internal CelesteNetPlayerSession(CelesteNetServer server, CelesteNetConnection con, uint sesId, string uid, string name, CelesteNetClientOptions clientOptions) {
            Server = server;
            Con = con;
            SessionID = sesId;
            ClientOptions = clientOptions;

            _Alive = 1;
            UID = uid;
            Name = name;

            LastWhisperSessionID = uint.MaxValue;

            Channel = server.Channels.Default;

            Interlocked.Increment(ref Server.PlayerCounter);
            Con.OnSendFilter += ConSendFilter;
            Con.OnReceiveFilter += ConRecvFilter;
            Server.Data.RegisterHandlersIn(this);
        }

        public bool CheckClientFeatureSupport(CelesteNetSupportedClientFeatures features) {
            return ClientOptions.SupportedClientFeatures.HasFlag(features);
        }

        public T? Get<T>(object ctx) where T : class {
            if (!Alive) {
                Logger.Log(LogLevel.INF, "playersession", $"Early return on attempt to 'Get<{typeof(T)}>' when session is already !Alive");
                return null;
            }

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

            if (!Alive) {
                Logger.Log(LogLevel.INF, "playersession", $"Early return on attempt to 'Set<{typeof(T)}>' when session is already !Alive");
                return state;
            }

            using (StateLock.W()) {
                if (!StateContexts.TryGetValue(ctx, out Dictionary<Type, object>? states))
                    StateContexts[ctx] = states = new();

                states[typeof(T)] = state;
                return state;
            }
        }

        public T? Remove<T>(object ctx) where T : class {
            if (!Alive) {
                Logger.Log(LogLevel.INF, "playersession", $"Early return on attempt to 'Remove<{typeof(T)}>' when session is already !Alive");
                return null;
            }

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
            Logger.Log(LogLevel.INF, "playersession", $"Startup #{SessionID} {Con} (Session UID: {UID}; Connection UID: {Con.UID})");
            Logger.Log(LogLevel.VVV, "playersession", $"Startup #{SessionID} @ {DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond} - Startup");

            string? clientDisconnectReason = null;
            if (Server.Settings.ClientChecks && Con is ConPlusTCPUDPConnection cpCon && cpCon.GetAssociatedData<ExtendedHandshake.ConnectionData>() is ExtendedHandshake.ConnectionData extConData)
                clientDisconnectReason = ExtendedHandshake.ClientCheck(cpCon, extConData);

            if (clientDisconnectReason != null) {
                Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} disconnecting because ClientCheck returned: '{clientDisconnectReason}'");
                Con.Send(new DataDisconnectReason { Text = clientDisconnectReason });
                Con.Send(new DataInternalDisconnect());
                Dispose();
                return;
            }

            // Resolver player name conflicts
            string nameSpace = Name;
            string fullNameSpace = nameSpace;
            string fullName = Name.Replace(" ", "");

            // This only checks against same clientID, not instanceID at the moment
            // i.e. currently you can only have one connection per installation, not per individual running instance...
            // but since we check for name being the same, you could set different guest names for many clients on same connection/installation
            if (ClientOptions.ClientID != 0) {
                using (Server.ConLock.R()) {
                    foreach (CelesteNetPlayerSession other in Server.Sessions) {
                        if ( other != this && other.Name == Name
                                           && other.UID == UID
                                           && other.Con.UID == Con.UID
                                           && other.ClientOptions.ClientID == ClientOptions.ClientID
                           ) {
                            // disconnect this client because this is a reconnecting client
                            other.Dispose();
                            other.Con.Send(new DataDisconnectReason { Text = "Connection resumed elsewhere." });
                            other.Con.Send(new DataInternalDisconnect());
                        }
                    }
                }
            }

            // generate more easily memorable persistent Guest name like "GuestDashingMadeline"
            if (Name == "Guest") {
                Random rnd = new Random(ClientOptions.ClientID != 0 ? (int) ClientOptions.ClientID : UID.GetHashCode());
                string prefix = "", character = "";
                while (prefix == character) {
                    prefix = rnd.Choose(GuestNamePrefixes);
                    character = rnd.Choose(GuestNameCharacter);
                }
                fullName = fullNameSpace = $"Guest{prefix}{character}";
            }

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

            // Handle avatars (+ crab day)
            string displayName = fullNameSpace;

            Stream? avatarStream;
            if (DateTime.Now is { Day: 1, Month: 4 } && File.Exists("crab.png"))
                // Time for crab :crab:
                avatarStream = File.OpenRead("crab.png");
            else
                avatarStream = Server.UserData.ReadFile(UID, "avatar.png");

            using (avatarStream) {
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

            Logger.Log(LogLevel.INF, "playersession", $"Session #{SessionID} PlayerInfo: {playerInfo} (UID: {UID}; Con: {Con.UID})");
            Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} @ {DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond}");

            // Send packets to players
            DataInternalBlob blobPlayerInfo = DataInternalBlob.For(Server.Data, playerInfo);

            Con.Send(playerInfo);
            Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} - Sent own PlayerInfo");

            foreach (DataInternalBlob fragBlob in AvatarFragments)
                Con.Send(fragBlob);
            Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} - Sent own Avatar frags");

            int blobSendsNew = 0, avaSendsNew = 0;
            int blobSendsOut = 0, avaSendsOut = 0;
            int boundSends = 0;
            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession other in Server.Sessions) {
                    if (other == this)
                        continue;

                    DataPlayerInfo? otherInfo = other.PlayerInfo;
                    if (otherInfo == null)
                        continue;

                    other.Con.Send(blobPlayerInfo);
                    blobSendsOut++;

                    if (!other.ClientOptions.AvatarsDisabled) {
                        foreach (DataInternalBlob fragBlob in AvatarFragments) {
                            other.Con.Send(fragBlob);
                            avaSendsOut++;
                        }
                    }

                    Con.Send(otherInfo);
                    blobSendsNew++;

                    if (!ClientOptions.AvatarsDisabled)
                        AvatarSendQueue.Add(other);
                }

            Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} - Done using ConLock -- blobSendsNew/avaSendsNew {blobSendsNew}/{avaSendsNew} - blobSendsOut/avaSendsOut {blobSendsOut}/{avaSendsOut} - boundSends {boundSends}");

            if (!Server.Channels.SessionStartupMove(this))
                ResendPlayerStates();

            Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} ClientID: {ClientOptions.ClientID} InstanceID: {ClientOptions.InstanceID}");
            Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} @ {DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond} - Sending DataReady");
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

            Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} - Doing player state resends");
            int boundPrivOut = 0, boundPublicOut = 0, boundPrivNew = 0;
            using (Server.ConLock.R())
                foreach (CelesteNetPlayerSession other in Server.Sessions) {
                    if (other == this)
                        continue;

                    foreach (DataType bound in boundAllPublic) {
                        other.Con.Send(bound);
                        boundPublicOut++;
                    }
                    foreach (DataType bound in boundAllPrivate) {
                        if (channel == other.Channel) {
                            other.Con.Send(bound);
                            boundPrivOut++;
                        }
                    }

                    DataPlayerInfo? otherInfo = other.PlayerInfo;
                    if (otherInfo == null)
                        continue;

                    foreach (DataType bound in Server.Data.GetBoundRefs(otherInfo))
                        if (!bound.Is<MetaPlayerPrivateState>(Server.Data) || channel == other.Channel) {
                            Con.Send(bound);
                            boundPrivNew++;
                        }
                }
            Logger.Log(LogLevel.VVV, "playersession", $"Session #{SessionID} - Done resends -- boundPrivOut/boundPublicOut {boundPrivOut}/{boundPublicOut} - boundPrivNew {boundPrivNew}");

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
            if (data is DataPlayerFrame frame) {
                frame.TransmitDashes = CheckClientFeatureSupport(CelesteNetSupportedClientFeatures.FrameWithDashes);
            }

            if (FilterList != null) {
                string source = data.GetSource(Server.Data);
                return string.IsNullOrEmpty(source) || FilterList.Contains(source);
            }

            return true;
        }
        public bool ConRecvFilter(CelesteNetConnection con, DataType data) {
            if (data is DataPlayerFrame frame) {
                frame.TransmitDashes = CheckClientFeatureSupport(CelesteNetSupportedClientFeatures.FrameWithDashes);
            }
            return true;
        }

        public void SendCommandList(DataCommandList commands) {
            if (commands == null || commands.List.Length == 0) {
                return;
            }

            // I almost made this a member variable of this class, but there's no point rn because it's only sent once at session start
            DataCommandList filteredCommands = new();

            bool auth = false;
            bool authExec = false;
            if (!(UID?.IsNullOrEmpty() ?? true) && Server.UserData.TryLoad(UID, out BasicUserInfo info)) {
                auth = info.Tags.Contains(BasicUserInfo.TAG_AUTH);
                authExec = info.Tags.Contains(BasicUserInfo.TAG_AUTH_EXEC);
            }

            filteredCommands.List = commands.List.Where(cmd => {
                    return (!cmd.Auth || auth)
                    && (!cmd.AuthExec || authExec)
                    && CheckClientFeatureSupport(cmd.RequiredFeatures);
                }).ToArray();

            Con.Send(filteredCommands);
        }

        public event Action<CelesteNetPlayerSession, DataPlayerInfo?>? OnEnd;

        public void Dispose() {
            if (Interlocked.Exchange(ref _Alive, 0) <= 0)
                return;

            Logger.Log(LogLevel.INF, "playersession", $"Shutdown #{SessionID} {Con} (Session UID: {UID}; PlayerInfo: {PlayerInfo})");

            DataPlayerInfo? playerInfoLast = PlayerInfo;

            if (playerInfoLast != null)
                Server.BroadcastAsync(new DataPlayerInfo {
                    ID = SessionID
                });

            Con.OnSendFilter -= ConSendFilter;
            Con.OnReceiveFilter -= ConRecvFilter;
            Server.Data.UnregisterHandlersIn(this);

            Server.Data.FreeRef<DataPlayerInfo>(SessionID);
            Server.Data.FreeOrder<DataPlayerFrame>(SessionID);

            OnEnd?.Invoke(this, playerInfoLast);

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
            // don't really need to resize arrays if they're bigger; it'll only send up to graphics.HairCount
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
        public void Handle(CelesteNetConnection con, DataNetFilterList list) {
            if (con != Con)
                return;

            FilterList = list;
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
