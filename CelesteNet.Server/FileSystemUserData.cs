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
    public class FileSystemUserData : UserData {

        public readonly object GlobalLock = new object();

        public string UserRoot => Path.Combine(Server.Settings.UserDataRoot, "User");
        public string GlobalPath => Path.Combine(Server.Settings.UserDataRoot, "Global.yaml");

        public FileSystemUserData(CelesteNetServer server)
            : base(server) {
        }

        public string GetUserDir(string uid)
            => Path.Combine(UserRoot, uid);

        public string GetUserFilePath(string uid, string name)
            => Path.Combine(UserRoot, uid, "data", name);

        public string GetUserFilePath<T>(string uid)
            => Path.Combine(UserRoot, uid, GetFileName<T>());

        public string GetFileName<T>()
            => (typeof(T)?.FullName ?? "unknown") + ".yaml";

        public bool TryLoadRaw<T>(string path, out T value) where T : new() {
            lock (GlobalLock) {
                if (!File.Exists(path)) {
                    value = new T();
                    return false;
                }

                using (Stream stream = File.OpenRead(path))
                using (StreamReader reader = new StreamReader(stream)) {
                    value = YamlHelper.Deserializer.Deserialize<T>(reader) ?? new T();
                    return true;
                }
            }
        }

        public T LoadRaw<T>(string path) where T : new()
            => TryLoadRaw(path, out T value) ? value : value;

        public void SaveRaw<T>(string path, T data) where T : notnull {
            lock (GlobalLock) {
                string? dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (Stream stream = File.OpenWrite(path + ".tmp"))
                using (StreamWriter writer = new StreamWriter(stream))
                    YamlHelper.Serializer.Serialize(writer, data, typeof(T));

                if (File.Exists(path))
                    File.Delete(path);
                File.Move(path + ".tmp", path);
            }
        }

        public void DeleteRaw(string path) {
            lock (GlobalLock) {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        public void DeleteRawAll(string path) {
            lock (GlobalLock) {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
        }

        public override string GetUID(string key) {
            lock (GlobalLock) {
                if (LoadRaw<Global>(GlobalPath).UIDs.TryGetValue(key, out string? uid))
                    return uid;
                return "";
            }
        }

        public override string GetKey(string uid)
            => Load<PrivateUserInfo>(uid).Key;

        public override bool TryLoad<T>(string uid, out T value)
            => TryLoadRaw(GetUserFilePath<T>(uid), out value);

        public override Stream? ReadFile(string uid, string name) {
            string path = GetUserFilePath(uid, name);
            if (!File.Exists(path))
                return null;
            return File.OpenRead(path);
        }

        public override void Save<T>(string uid, T value)
            => SaveRaw(GetUserFilePath<T>(uid), value);

        public override Stream WriteFile(string uid, string name) {
            string path = GetUserFilePath(uid, name);
            string? dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(path))
                File.Delete(path);
            return File.OpenWrite(path);
        }

        public override void Delete<T>(string uid) {
            DeleteRaw(GetUserFilePath<T>(uid));
            CheckCleanup(uid);
        }

        public override void DeleteFile(string uid, string name) {
            string path = GetUserFilePath(uid, name);
            if (File.Exists(path))
                File.Delete(path);
            CheckCleanup(uid);
        }

        private void CheckCleanup(string uid) {
            string dir = GetUserDir(uid);
            if (Directory.GetFiles(dir).Length == 0)
                DeleteRawAll(dir);
        }

        public override void Wipe(string uid)
            => DeleteRawAll(GetUserDir(uid));

        public override T[] LoadRegistered<T>() {
            lock (GlobalLock) {
                return LoadRaw<Global>(GlobalPath).UIDs.Values.Select(uid => Load<T>(uid)).ToArray();
            }
        }

        public override T[] LoadAll<T>() {
            lock (GlobalLock) {
                string name = GetFileName<T>();
                return Directory.GetDirectories(UserRoot).Select(dir => LoadRaw<T>(Path.Combine(dir, name))).ToArray();
            }
        }

        public override string[] GetRegistered()
            => LoadRaw<Global>(GlobalPath).UIDs.Values.ToArray();

        public override string[] GetAll()
            => Directory.GetDirectories(UserRoot).Select(name => Path.GetFileName(name)).ToArray();

        public override int GetRegisteredCount()
            => LoadRaw<Global>(GlobalPath).UIDs.Count;

        public override int GetAllCount()
            => Directory.GetDirectories(UserRoot).Length;

        public override string Create(string uid) {
            lock (GlobalLock) {
                Global global = LoadRaw<Global>(GlobalPath);
                string key = GetKey(uid);
                if (!key.IsNullOrEmpty())
                    return key;

                string keyFull;
                do {
                    keyFull = Guid.NewGuid().ToString().Replace("-", "");
                    key = keyFull.Substring(0, 16);
                } while (global.UIDs.ContainsKey(key));
                global.UIDs[key] = uid;

                Save(uid, new PrivateUserInfo {
                    Key = key,
                    KeyFull = keyFull
                });

                SaveRaw(GlobalPath, global);

                return key;
            }
        }

        public override void RevokeKey(string key) {
            lock (GlobalLock) {
                Global global = LoadRaw<Global>(GlobalPath);
                if (!global.UIDs.TryGetValue(key, out string? uid))
                    return;

                global.UIDs.Remove(key);
                SaveRaw(GlobalPath, global);

                Delete<PrivateUserInfo>(uid);
            }
        }

        public class Global {
            public Dictionary<string, string> UIDs { get; set; } = new Dictionary<string, string>();
        }

        public class PrivateUserInfo {
            public string Key { get; set; } = "";
            public string KeyFull { get; set; } = "";
        }

    }
}
