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
    public abstract class UserData {

        public readonly CelesteNetServer Server;

        public UserData(CelesteNetServer server) {
            Server = server;
        }

        public abstract string GetUID(string key);
        public abstract string GetKey(string uid);

        public abstract bool TryLoad<T>(string uid, out T value) where T : new();
        public T Load<T>(string uid) where T : new()
            => TryLoad(uid, out T value) ? value : value;
        public abstract void Save<T>(string uid, T value) where T : notnull;

        public abstract void Delete<T>(string uid);
        public abstract void DeleteAll(string uid);

        public abstract string Create(string uid);

    }

    public class BasicUserInfo {
        public string Name = "";
        public HashSet<string> Tags = new HashSet<string>();
    }

    public class BanInfo {
        public string Reason = "";
        public DateTime? From = null;
        public DateTime? To = null;
    }
}
