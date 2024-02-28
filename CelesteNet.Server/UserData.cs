using System;
using System.Collections.Generic;
using System.IO;

namespace Celeste.Mod.CelesteNet.Server
{
    public abstract class UserData : IDisposable {

        public readonly CelesteNetServer Server;

        public UserData(CelesteNetServer server) {
            Server = server;
        }

        public abstract void Dispose();

        public virtual UserDataBatchContext OpenBatch() => UserDataBatchContext.Nop;

        public abstract string GetUID(string key);
        public abstract string GetKey(string uid);

        public abstract bool TryLoad<T>(string uid, out T value) where T : new();
        public T Load<T>(string uid) where T : new()
            => TryLoad(uid, out T value) ? value : value;
        public abstract bool HasFile(string uid, string name);
        public abstract Stream? ReadFile(string uid, string name);
        public abstract void Save<T>(string uid, T value) where T : notnull;
        public abstract Stream WriteFile(string uid, string name);
        public abstract void Delete<T>(string uid);
        public abstract void DeleteFile(string uid, string name);
        public abstract void Wipe(string uid);

        public abstract T[] LoadRegistered<T>() where T : new();
        public abstract T[] LoadAll<T>() where T : new();

        public abstract string[] GetRegistered();
        public abstract string[] GetAll();

        public abstract int GetRegisteredCount();
        public abstract int GetAllCount();

        public abstract string Create(string uid, bool forceNewKey);
        public abstract void RevokeKey(string key);

        public abstract void CopyTo(UserData other);
        public abstract void Insert(string uid, string key, string keyFull, bool registered);
        public abstract void InsertData(string uid, string name, Type? type, Stream stream);
        public abstract void InsertFile(string uid, string name, Stream stream);

    }

    public class UserDataBatchContext : IDisposable {

        public static readonly UserDataBatchContext Nop = new();

        protected UserDataBatchContext() {
        }

        public virtual void Dispose() {
        }

    }

    public class BasicUserInfo {
        public static readonly string TAG_AUTH = "moderator";
        public static readonly string TAG_AUTH_EXEC = "admin";
        public static readonly IReadOnlyList<string> AUTH_TAGS = new List<string>() {
            TAG_AUTH,
            TAG_AUTH_EXEC
        }.AsReadOnly();

        public string Name { get; set; } = "";
        // TODO: Move into separate Discord module!
        public string Discrim { get; set; } = "";
        public HashSet<string> Tags { get; set; } = new();
    }

    public class BanInfo {
        public string UID { get; set; } = "";
        public string Name { get; set; } = "";
        public string Reason { get; set; } = "";
        public DateTime? From { get; set; } = null;
        public DateTime? To { get; set; } = null;
    }

    public class KickHistory {
        public List<Entry> Log { get; set; } = new();
        public class Entry {
            public string Reason { get; set; } = "";
            public DateTime? From { get; set; } = null;
        }
    }
}
