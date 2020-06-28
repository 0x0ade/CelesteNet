﻿using Celeste.Mod.CelesteNet.DataTypes;
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

        public string GlobalMapPath => Path.Combine(Server.Settings.UserDataRoot, "globalmap.yaml");

        public FileSystemUserData(CelesteNetServer server)
            : base(server) {
        }

        public string GetUserDir(string uid)
            => Path.Combine(Server.Settings.UserDataRoot, "user", uid);

        public string GetUserFilePath<T>(string uid)
            => Path.Combine(Server.Settings.UserDataRoot, "user", uid, typeof(T).FullName);

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
                string dir = Path.GetDirectoryName(path);
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
                if (LoadRaw<GlobalMap>(GlobalMapPath).UIDs.TryGetValue(key, out string? uid))
                    return uid;
                return "";
            }
        }

        public override string GetKey(string uid)
            => Load<PrivateUserInfo>(uid).Key;

        public override bool TryLoad<T>(string uid, out T value)
            => TryLoadRaw(GetUserFilePath<T>(uid), out value);

        public override void Save<T>(string uid, T value)
            => SaveRaw(GetUserFilePath<T>(uid), value);

        public override void Delete<T>(string uid)
            => DeleteRaw(GetUserFilePath<T>(uid));

        public override void DeleteAll(string uid)
            => DeleteRawAll(GetUserDir(uid));

        public override string Create(string uid) {
            lock (GlobalLock) {
                GlobalMap globalmap = LoadRaw<GlobalMap>(GlobalMapPath);
                if (globalmap.UIDs.TryGetValue(uid, out string? key))
                    return key;

                string keyFull;
                do {
                    keyFull = Guid.NewGuid().ToString().Replace("-", "");
                    key = keyFull.Substring(0, 16);
                } while (globalmap.UsedKeys.Contains(key));
                globalmap.UsedKeys.Add(key);

                Save(uid, new PrivateUserInfo {
                    Key = key,
                    KeyFull = keyFull
                });

                SaveRaw(GlobalMapPath, globalmap);

                return key;
            }
        }

        public class GlobalMap {
            public Dictionary<string, string> UIDs = new Dictionary<string, string>();
            public HashSet<string> UsedKeys = new HashSet<string>();
        }

        public class PrivateUserInfo {
            public string Key = "";
            public string KeyFull = "";
        }

    }
}