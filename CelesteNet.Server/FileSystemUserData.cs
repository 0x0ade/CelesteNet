using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Celeste.Mod.CelesteNet.Server
{
    public class FileSystemUserData : UserData {

        public readonly object GlobalLock = new();

        public string UserRoot => Path.Combine(Server.Settings.UserDataRoot, "User");
        public string GlobalPath => Path.Combine(Server.Settings.UserDataRoot, "Global.yaml");

        public FileSystemUserData(CelesteNetServer server)
            : base(server) {
        }

        public override void Dispose() {
        }

        public string GetUserDir(string uid)
            => Path.Combine(UserRoot, uid);

        public string GetUserDataFilePath(string uid, Type type)
            => Path.Combine(UserRoot, uid, GetDataFileName(type));

        public string GetUserDataFilePath(string uid, string name)
            => Path.Combine(UserRoot, uid, name + ".yaml");

        public string GetUserFilePath(string uid, string name)
            // Misnomer: "data" in this case should be "raw". Can't change without breaking compat tho.
            => Path.Combine(UserRoot, uid, "data", name);

        public static string GetDataFileName(Type type)
            => (type?.FullName ?? "unknown") + ".yaml";

        public bool TryLoadRaw<T>(string path, out T value) where T : new() {
            lock (GlobalLock) {
                if (!File.Exists(path)) {
                    value = new();
                    return false;
                }

                using Stream stream = File.OpenRead(path);
                using StreamReader reader = new(stream);
                value = YamlHelper.Deserializer.Deserialize<T>(reader) ?? new();
                return true;
            }
        }

        public T LoadRaw<T>(string path) where T : new()
            => TryLoadRaw(path, out T value) ? value : value;

        public void SaveRaw<T>(string path, T data) where T : notnull {
            lock (GlobalLock) {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using (Stream stream = File.OpenWrite(path + ".tmp"))
                using (StreamWriter writer = new(stream))
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
            if (key.IsNullOrEmpty())
                return "";
            lock (GlobalLock) {
                if (LoadRaw<Global>(GlobalPath).UIDs.TryGetValue(key, out string? uid))
                    return uid;
                return "";
            }
        }

        public override string GetKey(string uid)
            => Load<PrivateUserInfo>(uid).Key;

        public override bool TryLoad<T>(string uid, out T value)
            => TryLoadRaw(GetUserDataFilePath(uid, typeof(T)), out value);

        public override bool HasFile(string uid, string name) {
            return File.Exists(GetUserDataFilePath(uid, name));
        }

        public override Stream? ReadFile(string uid, string name) {
            string path = GetUserFilePath(uid, name);
            if (!File.Exists(path))
                return null;
            return File.OpenRead(path);
        }

        public override void Save<T>(string uid, T value)
            => SaveRaw(GetUserDataFilePath(uid, typeof(T)), value);

        public override Stream WriteFile(string uid, string name) {
            string path = GetUserFilePath(uid, name);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (File.Exists(path))
                File.Delete(path);
            return File.OpenWrite(path);
        }

        public override void Delete<T>(string uid) {
            DeleteRaw(GetUserDataFilePath(uid, typeof(T)));
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
                if (!Directory.Exists(UserRoot))
                    return Dummy<T>.EmptyArray;
                string name = GetDataFileName(typeof(T));
                return Directory.GetDirectories(UserRoot).Select(dir => LoadRaw<T>(Path.Combine(dir, name))).ToArray();
            }
        }

        public override string[] GetRegistered()
            => LoadRaw<Global>(GlobalPath).UIDs.Values.ToArray();

        public override string[] GetAll()
            => !Directory.Exists(UserRoot) ? Dummy<string>.EmptyArray : Directory.GetDirectories(UserRoot).Select(name => Path.GetFileName(name)).ToArray();

        public override int GetRegisteredCount()
            => LoadRaw<Global>(GlobalPath).UIDs.Count;

        public override int GetAllCount()
            => Directory.GetDirectories(UserRoot).Length;

        public override string Create(string uid, bool forceNewKey) {
            lock (GlobalLock) {
                Global global = LoadRaw<Global>(GlobalPath);
                string key = GetKey(uid);
                if (!key.IsNullOrEmpty() && !forceNewKey)
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

        public override void CopyTo(UserData other) {
            using UserDataBatchContext batch = other.OpenBatch();
            lock (GlobalLock) {
                Global global = LoadRaw<Global>(GlobalPath);

                Dictionary<string, Type?> types = new();
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();

                foreach (string uid in GetAll()) {
                    PrivateUserInfo info = Load<PrivateUserInfo>(uid);
                    other.Insert(uid, info.Key, info.KeyFull, !info.KeyFull.IsNullOrEmpty());

                    foreach (string path in Directory.GetFiles(Path.Combine(UserRoot, uid))) {
                        string name = Path.GetFileNameWithoutExtension(path);
                        if (name == typeof(PrivateUserInfo).FullName)
                            continue;

                        if (!types.TryGetValue(name, out Type? type)) {
                            foreach (Assembly asm in asms)
                                if ((type = asm.GetType(name)) != null)
                                    break;
                            types[name] = type;
                        }

                        using Stream stream = File.OpenRead(path);
                        other.InsertData(uid, name, type, stream);
                    }

                    string dir = Path.Combine(UserRoot, uid, "data");
                    if (Directory.Exists(dir)) {
                        foreach (string path in Directory.GetFiles(dir)) {
                            string name = Path.GetFileName(path);
                            using Stream stream = File.OpenRead(path);
                            other.InsertFile(uid, name, stream);
                        }
                    }
                }
            }
        }

        public override void Insert(string uid, string key, string keyFull, bool registered) {
            lock (GlobalLock) {
                Global global = LoadRaw<Global>(GlobalPath);
                global.UIDs[key] = uid;
                SaveRaw(GlobalPath, global);

                Save<PrivateUserInfo>(uid, new() {
                    Key = key,
                    KeyFull = keyFull
                });
            }
        }

        private void InsertFileRaw(string path, Stream stream) {
            lock (GlobalLock) {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(path))
                    File.Delete(path);
                Stream target = File.OpenWrite(path);
                stream.CopyTo(target);
            }
        }

        public override void InsertData(string uid, string name, Type? type, Stream stream)
            => InsertFileRaw(type != null ? GetUserDataFilePath(uid, type) : GetUserDataFilePath(uid, name), stream);

        public override void InsertFile(string uid, string name, Stream stream)
            => InsertFileRaw(GetUserFilePath(uid, name), stream);

        public class Global {
            public Dictionary<string, string> UIDs { get; set; } = new();
        }

        public class PrivateUserInfo {
            public string Key { get; set; } = "";
            public string KeyFull { get; set; } = "";
        }

    }
}
