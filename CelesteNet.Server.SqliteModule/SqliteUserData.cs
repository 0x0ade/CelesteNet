using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MessagePack;
using Microsoft.Data.Sqlite;

namespace Celeste.Mod.CelesteNet.Server.Sqlite {
    public sealed class SqliteUserData : UserData {

        public static readonly HashSet<char> Illegal = new("`´'\"^[]\\//");

        public readonly object GlobalLock = new();

        public readonly SqliteModule Module;

        private string? _userDataRoot;
        public string UserDataRoot => _userDataRoot ??= Module.Settings.UserDataRoot;

        private string? _dbName;
        public string DBName => _dbName ??= "main.db";

        public string DBPath => Path.Combine(UserDataRoot, DBName);

        private readonly ThreadLocal<BatchContext> _Batch = new();
        public BatchContext Batch => _Batch.Value ??= new(this);

        public SqliteUserData(SqliteModule module, string? userDataRoot = null, string? dbName = null)
            : base(module.Server) {
            Module = module;

            _userDataRoot = userDataRoot;
            _dbName = dbName;

            if (!Directory.Exists(UserDataRoot))
                Directory.CreateDirectory(UserDataRoot);

            if (!File.Exists(DBPath)) {
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadWriteCreate,
                    @"
                        -- sqlite can only into one database.
                        -- CREATE DATABASE main;

                        CREATE TABLE [meta] (
                            iid INTEGER PRIMARY KEY AUTOINCREMENT,
                            uid VARCHAR(255) UNIQUE,
                            key VARCHAR(255),
                            keyfull VARCHAR(255),
                            registered BOOLEAN
                        );

                        CREATE TABLE [data] (
                            iid INTEGER PRIMARY KEY AUTOINCREMENT,
                            name VARCHAR(255) UNIQUE,
                            real VARCHAR(255) UNIQUE,
                            type VARCHAR(255) UNIQUE
                        );

                        CREATE TABLE [file] (
                            iid INTEGER PRIMARY KEY AUTOINCREMENT,
                            name VARCHAR(255) UNIQUE,
                            real VARCHAR(255) UNIQUE
                        );
                    "
                };
                mini.Run();
            }
        }

        public override void Dispose() {
        }

        public override UserDataBatchContext OpenBatch() {
            BatchContext batch = Batch;
            batch.Count++;
            return batch;
        }

        public SqliteConnection Open(SqliteOpenMode mode) {
            SqliteConnection con = new(new SqliteConnectionStringBuilder() {
                DataSource = DBPath,
                Mode = mode,
            }.ToString());
            con.Open();
            return con;
        }

        public List<string> GetAllTables() {
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @"
                    SELECT name
                    FROM sqlite_master
                    WHERE type = 'table';
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            List<string> names = new();
            while (reader.Read())
                names.Add(reader.GetString(0));

            return names;
        }

        public string GetDataTable(Type type, bool create) {
            type ??= typeof(object);
            string real = type.FullName ?? throw new Exception($"Type without full name: {type}");

            // FIXME: AAAAAAAAAAAAAAAAAAAAAAAAAA
            string name = $"data.{real}".Sanitize(Illegal);

            if (create) {
                string typename = type.AssemblyQualifiedName ?? throw new Exception($"Type without assembly qualified name: {type}");
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadWrite,
                    @$"
                        CREATE TABLE IF NOT EXISTS [{name}] (
                            iid INTEGER PRIMARY KEY AUTOINCREMENT,
                            uid VARCHAR(255) UNIQUE,
                            format INTEGER,
                            value BLOB
                        );

                        INSERT OR IGNORE INTO data (name, real, type)
                        VALUES ($name, $real, $type);

                        SELECT name
                        FROM sqlite_master
                        WHERE type = 'table'
                        AND name = $name;
                    ",
                    { "$name", name },
                    { "$real", real },
                    { "$type", typename },
                };
                return mini.Run<string>().value ?? "";

            } else {
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadOnly,
                    @"
                        SELECT name
                        FROM sqlite_master
                        WHERE type = 'table'
                        AND name = $name;
                    ",
                    { "$name", name }
                };
                return mini.Run<string>().value ?? "";
            }
        }

        public string GetDataTable(string shortname, bool create) {
            // FIXME: AAAAAAAAAAAAAAAAAAAAAAAAAA
            string name = $"data.{shortname}".Sanitize(Illegal);

            if (create) {
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadWrite,
                    @$"
                        CREATE TABLE IF NOT EXISTS [{name}] (
                            iid INTEGER PRIMARY KEY AUTOINCREMENT,
                            uid VARCHAR(255) UNIQUE,
                            format INTEGER,
                            value BLOB
                        );

                        -- The data table name -> type name mapping will be set once GetDataTable(Type) is called.
                        -- INSERT OR IGNORE INTO data (name, real, type)
                        -- VALUES ($name, $real, $type);

                        SELECT name
                        FROM sqlite_master
                        WHERE type = 'table'
                        AND name = $name;
                    ",
                    { "$name", name },
                };
                return mini.Run<string>().value ?? "";

            } else {
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadOnly,
                    @"
                        SELECT name
                        FROM sqlite_master
                        WHERE type = 'table'
                        AND name = $name;
                    ",
                    { "$name", name },
                };
                return mini.Run<string>().value ?? "";
            }
        }

        public string GetFileTable(string name, bool create) {
            string real = name;

            // FIXME: AAAAAAAAAAAAAAAAAAAAAAAAAA
            name = $"file.{real}".Sanitize(Illegal);

            if (create) {
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadWrite,
                    @$"
                        CREATE TABLE IF NOT EXISTS [{name}] (
                            iid INTEGER PRIMARY KEY AUTOINCREMENT,
                            uid VARCHAR(255) UNIQUE,
                            value BLOB
                        );

                        INSERT OR IGNORE INTO file (name, real)
                        VALUES ($name, $real);

                        SELECT name
                        FROM sqlite_master
                        WHERE type = 'table'
                        AND name = $name;
                    ",
                    { "$name", name },
                    { "$real", real },
                };
                return mini.Run<string>().value ?? "";

            } else {
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadOnly,
                    @"
                        SELECT name
                        FROM sqlite_master
                        WHERE type = 'table'
                        AND name = $name;
                    ",
                    { "$name", name },
                };
                return mini.Run<string>().value ?? "";
            }
        }

        public override string GetUID(string key) {
            if (key.IsNullOrEmpty())
                return "";
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @"
                    SELECT uid
                    FROM meta
                    WHERE key = $key
                    LIMIT 1;
                ",
                { "$key", key },
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();
            return reader.Read() ? reader.GetString(0) : "";
        }

        public override string GetKey(string uid) {
            if (uid.IsNullOrEmpty())
                return "";
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @"
                    SELECT key
                    FROM meta
                    WHERE uid = $uid
                    LIMIT 1;
                ",
                { "$uid", uid },
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();
            return reader.Read() ? reader.GetString(0) : "";
        }

        public override bool TryLoad<T>(string uid, out T value) {
            using UserDataBatchContext batch = OpenBatch();
            string table = GetDataTable(typeof(T), false);
            if (table.IsNullOrEmpty()) {
                value = new();
                return false;
            }
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @$"
                    SELECT format, value
                    FROM [{table}]
                    WHERE uid = $uid
                    LIMIT 1;
                ",
                { "$uid", uid },
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            if (!reader.Read()) {
                value = new();
                return false;
            }

            DataFormat format = (DataFormat) reader.GetInt32(0);
            try {
                switch (format) {
                    case DataFormat.MessagePack:
                    default: {
                        using Stream stream = reader.GetStream(1);
                        value = MessagePackSerializer.Deserialize<T>(stream, MessagePackHelper.Options) ?? new();
                        return true;
                    }

                    case DataFormat.Yaml: {
                        using Stream stream = reader.GetStream(1);
                        using StreamReader streamReader = new(stream);
                        value = YamlHelper.Deserializer.Deserialize<T>(streamReader) ?? new();
                        return true;
                    }
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.ERR, "sqlite", $"Failed loading data UID \"{uid}\" type {typeof(T).FullName} format {format}:\n{e}");
                value = new();
                return false;
            }
        }

        public override bool HasFile(string uid, string name) {
            using UserDataBatchContext batch = OpenBatch();
            string table = GetFileTable(name, false);
            if (table.IsNullOrEmpty())
                return false;
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @$"
                    SELECT value
                    FROM [{table}]
                    WHERE uid = $uid
                    LIMIT 1;
                ",
                { "$uid", uid },
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            if (!reader.Read())
                return false;

            return true;
        }

        public override Stream? ReadFile(string uid, string name) {
            using UserDataBatchContext batch = OpenBatch();
            string table = GetFileTable(name, false);
            if (table.IsNullOrEmpty())
                return null;
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @$"
                    SELECT value
                    FROM [{table}]
                    WHERE uid = $uid
                    LIMIT 1;
                ",
                { "$uid", uid },
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            if (!reader.Read())
                return null;

            Stream stream = reader.GetStream(0);
            if (stream is MemoryStream ms)
                return ms;

            ms = new();
            using (stream)
                stream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        public override void Save<T>(string uid, T value) {
            using UserDataBatchContext batch = OpenBatch();
            using MemoryStream ms = new();
            MessagePackSerializer.Serialize(typeof(T), ms, value, MessagePackHelper.Options);

            ms.Seek(0, SeekOrigin.Begin);

            string table = GetDataTable(typeof(T), true);
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadWrite,
                EnsureExistsQuery + @$"
                    REPLACE INTO [{table}] (uid, format, value)
                    VALUES ($uid, $format, zeroblob($length));

                    SELECT _ROWID_
                    FROM [{table}]
                    WHERE uid = $uid;
                ",
                { "$uid", uid },
                { "$format", (int) DataFormat.MessagePack },
                { "$length", ms.Length },
            };
            (SqliteConnection con, SqliteCommand cmd) = mini.Run(out long? rowid);

            if (rowid == null) {
                Logger.Log(LogLevel.ERR, "sqlite", $"Save<T>: Failed getting rowid for {table} of UID {uid}");
                return;
            }

            using SqliteBlob blob = new(con, table, "value", (long) rowid);
            ms.CopyTo(blob);
            blob.Dispose();
        }

        public override Stream WriteFile(string uid, string name) {
            MemoryStream ms = new();
            return new DisposeActionStream(ms, () => {
                ms.Seek(0, SeekOrigin.Begin);

                using UserDataBatchContext batch = OpenBatch();
                string table = GetFileTable(name, true);
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadWrite,
                    EnsureExistsQuery + @$"
                        REPLACE INTO [{table}] (uid, value)
                        VALUES ($uid, zeroblob($length));

                        SELECT _ROWID_
                        FROM [{table}]
                        WHERE uid = $uid;
                    ",
                    { "$uid", uid },
                    { "$length", ms.Length },
                };
                (SqliteConnection con, SqliteCommand cmd) = mini.Run(out long? rowid);

                if (rowid == null) {
                    Logger.Log(LogLevel.ERR, "sqlite", $"WriteFile: Failed getting rowid for {table} of UID {uid}");
                    return;
                }

                using SqliteBlob blob = new(con, table, "value", (long) rowid);
                ms.CopyTo(blob);
                blob.Dispose();
            });
        }

        public override void Delete<T>(string uid) {
            using UserDataBatchContext batch = OpenBatch();
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadWrite,
                @$"
                    DELETE
                    FROM [{GetDataTable(typeof(T), true)}]
                    WHERE uid = $uid;
                ",
                { "$uid", uid },
            };
            mini.Run();

            CheckCleanup(uid);
        }

        public override void DeleteFile(string uid, string name) {
            using UserDataBatchContext batch = OpenBatch();
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadWrite,
                @$"
                    DELETE
                    FROM [{GetFileTable(name, true)}]
                    WHERE uid = $uid;
                ",
                { "$uid", uid },
            };
            mini.Run();

            CheckCleanup(uid);
        }

        private void CheckCleanup(string uid) {
            using UserDataBatchContext batch = OpenBatch();
            using MiniCommand mini = new(this);

            mini.Next(new() {
                SqliteOpenMode.ReadOnly,
                @$"
                    SELECT registered
                    FROM meta
                    WHERE uid = $uid
                    LIMIT 1;
                ",
                { "$uid", uid },
            });
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            if (!reader.Read() || reader.GetBoolean(0))
                return;

            foreach (string table in GetAllTables()) {
                if (table == "meta")
                    continue;

                mini.Next(new() {
                SqliteOpenMode.ReadOnly,
                    $@"
                        SELECT iid
                        FROM [{table}]
                        WHERE uid = $uid
                        LIMIT 1;
                    ",
                    { "$uid", uid },
                });
                (con, cmd, reader) = mini.Read();

                while (reader.Read())
                    if (!reader.IsDBNull(0))
                        return;
            }

            Wipe(uid);
        }

        public override void Wipe(string uid) {
            using UserDataBatchContext batch = OpenBatch();
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadWrite,
                string.Join('\n', GetAllTables().Select(table => $@"
                    DELETE
                    FROM [{table}]
                    WHERE uid = $uid;
                ")),
                { "$uid", uid },
            };
            mini.Run();
        }

        public override Dictionary<string, T> LoadRegistered<T>() {
            using UserDataBatchContext batch = OpenBatch();
            string table = GetDataTable(typeof(T), false);
            if (table.IsNullOrEmpty())
                return new Dictionary<string, T>();

            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @$"
                    SELECT D.uid, D.format, D.value
                    FROM [{table}] D
                    INNER JOIN meta M ON D.uid = M.uid
                    WHERE M.registered = 1;
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            Dictionary<string, T> values = new();
            while (reader.Read()) {
                string uid = reader.GetString(0);

                switch ((DataFormat) reader.GetInt32(1)) {
                    case DataFormat.MessagePack:
                    default: {
                        using Stream stream = reader.GetStream(2);
                        values.Add(uid, MessagePackSerializer.Deserialize<T>(stream, MessagePackHelper.Options) ?? new());
                        break;
                    }

                    case DataFormat.Yaml: {
                        using Stream stream = reader.GetStream(2);
                        using StreamReader streamReader = new(stream);
                        values.Add(uid, YamlHelper.Deserializer.Deserialize<T>(streamReader) ?? new());
                        break;
                    }
                }
            }

            return values;
        }

        public override Dictionary<string, T> LoadAll<T>() {
            using UserDataBatchContext batch = OpenBatch();
            string table = GetDataTable(typeof(T), false);
            if (table.IsNullOrEmpty())
                return new Dictionary<string, T>();

            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @$"
                    SELECT uid, format, value
                    FROM [{table}];
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            Dictionary<string, T> values = new();
            while (reader.Read()) {
                string uid = reader.GetString(0);

                switch ((DataFormat) reader.GetInt32(1)) {
                    case DataFormat.MessagePack:
                    default: {
                        using Stream stream = reader.GetStream(2);
                        values.Add(uid, MessagePackSerializer.Deserialize<T>(stream, MessagePackHelper.Options) ?? new());
                        break;
                    }

                    case DataFormat.Yaml: {
                        using Stream stream = reader.GetStream(2);
                        using StreamReader streamReader = new(stream);
                        values.Add(uid, YamlHelper.Deserializer.Deserialize<T>(streamReader) ?? new());
                        break;
                    }
                }
            }

            return values;
        }

        public override string[] GetRegistered() {
            using UserDataBatchContext batch = OpenBatch();
            string[] uids = new string[GetRegisteredCount()];
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @"
                    SELECT uid
                    FROM meta
                    WHERE registered = 1;
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();
            for (int i = 0; i < uids.Length && reader.Read(); i++)
                uids[i] = reader.GetString(0);
            return uids;
        }

        public override string[] GetAll() {
            using UserDataBatchContext batch = OpenBatch();
            string[] uids = new string[GetAllCount()];
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @"
                    SELECT uid
                    FROM meta;
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();
            for (int i = 0; i < uids.Length && reader.Read(); i++)
                uids[i] = reader.GetString(0);
            return uids;
        }

        public override int GetRegisteredCount() {
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @"
                    SELECT COUNT(uid)
                    FROM meta
                    WHERE registered = 1;
                ",
            };
            return (int) mini.Run<long>().value;
        }

        public override int GetAllCount() {
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadOnly,
                @"
                    SELECT COUNT(uid)
                    FROM meta;
                ",
            };
            return (int) mini.Run<long>().value;
        }

        public static readonly string EnsureExistsQuery = @"
            INSERT OR IGNORE INTO meta (uid, key, keyfull, registered)
            VALUES ($uid, '', '', 0);
        ";
        public void EnsureExists(string uid) {
            lock (GlobalLock) {
                using MiniCommand mini = new(this) {
                    EnsureExistsQuery,
                    { "$uid", uid },
                };
                mini.Run();
            }
        }

        public override string Create(string uid, bool forceNewKey) {
            lock (GlobalLock) {
                string key = GetKey(uid);
                if (!key.IsNullOrEmpty() && !forceNewKey)
                    return key;

                string keyFull;
                do {
                    keyFull = Guid.NewGuid().ToString().Replace("-", "");
                    key = keyFull.Substring(0, 16);
                } while (!GetUID(key).IsNullOrEmpty());

                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadWrite,
                    @"
                        REPLACE INTO meta (uid, key, keyfull, registered)
                        VALUES ($uid, $key, $keyfull, 1);
                    ",
                    { "$uid", uid },
                    { "$key", key },
                    { "$keyfull", keyFull },
                };
                mini.Run();

                return key;
            }
        }

        public override void RevokeKey(string key) {
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadWrite,
                @"
                    UPDATE meta
                    SET key = ''
                    WHERE key = $key;
                ",
                { "$key", key },
            };
            mini.Run();
        }

        public override void CopyTo(UserData other) {
            using UserDataBatchContext batchOther = other.OpenBatch();
            using UserDataBatchContext batch = OpenBatch();
            lock (GlobalLock) {
                MiniCommand mini = new(this);

                mini.Next(new() {
                    SqliteOpenMode.ReadOnly,
                    $@"
                        SELECT uid, key, keyfull, registered
                        FROM meta;
                    "
                });
                (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();
                List<string> uids = new();
                while (reader.Read())
                    other.Insert(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));

                foreach (string table in GetAllTables()) {
                    bool data = table.StartsWith("data.");
                    bool file = table.StartsWith("file.");
                    if (!data && !file)
                        continue;

                    mini.Next(new() {
                        SqliteOpenMode.ReadOnly,
                        $@"
                            SELECT uid, value
                            FROM [{table}];
                        ",
                    });
                    (con, cmd, reader) = mini.Read();

                    if (data) {
                        using MiniCommand miniName = new(this) {
                            mini.Connection,
                            SqliteOpenMode.ReadOnly,
                            @"
                                SELECT real, format, type
                                FROM data
                                WHERE name = $name;
                            ",
                            { "$name", table }
                        };
                        (_, _, SqliteDataReader readerName) = miniName.Read();
                        miniName.Add((SqliteConnection?) null);
                        if (!readerName.Read())
                            continue;
                        string name = readerName.GetString(0);
                        string typeName = readerName.GetString(1);
                        Type? type = Type.GetType(typeName);

                        while (reader.Read()) {
                            string uid = reader.GetString(0);
                            switch ((DataFormat) reader.GetInt32(1)) {
                                case DataFormat.MessagePack:
                                default: {
                                    if (type == null) {
                                        // TODO: Cannot transform data from MessagePack to Yaml!
                                    } else {
                                        using Stream stream = reader.GetStream(2);
                                        object? value = MessagePackSerializer.Deserialize(type, stream, MessagePackHelper.Options);
                                        using MemoryStream ms = new();
                                        using StreamWriter msWriter = new(ms);
                                        YamlHelper.Serializer.Serialize(msWriter, value);
                                        ms.Seek(0, SeekOrigin.Begin);
                                        other.InsertData(uid, name, type, ms);
                                    }
                                    break;
                                }

                                case DataFormat.Yaml: {
                                    using Stream stream = reader.GetStream(2);
                                    other.InsertData(uid, name, type, stream);
                                    break;
                                }
                            }
                        }
                    } else if (file) {
                        using MiniCommand miniName = new(this) {
                            mini.Connection,
                            SqliteOpenMode.ReadOnly,
                            @"
                                SELECT real
                                FROM file
                                WHERE name = $name;
                            ",
                            { "$name", table }
                        };
                        (_, _, SqliteDataReader readerName) = miniName.Read();
                        miniName.Add((SqliteConnection?) null);
                        if (!readerName.Read())
                            continue;
                        string name = readerName.GetString(0);

                        while (reader.Read()) {
                            string uid = reader.GetString(0);
                            using Stream stream = reader.GetStream(1);
                            other.InsertFile(uid, name, stream);
                        }

                    } else {
                        // ??
                    }
                }
            }
        }

        public override void Insert(string uid, string key, string keyFull, bool registered) {
            lock (GlobalLock) {
                using MiniCommand mini = new(this) {
                    SqliteOpenMode.ReadWrite,
                    @"
                        REPLACE INTO meta (uid, key, keyfull, registered)
                        VALUES ($uid, $key, $keyfull, $registered);
                    ",
                    { "$uid", uid },
                    { "$key", key },
                    { "$keyfull", keyFull },
                    { "$registered", registered },
                };
                mini.Run();
            }
        }

        public override void InsertData(string uid, string name, Type? type, Stream stream) {
            using MemoryStream ms = new();
            DataFormat format;

            if (type == null) {
                format = DataFormat.Yaml;
                stream.CopyTo(ms);

            } else {
                format = DataFormat.MessagePack;
                using StreamReader reader = new(stream);
                object? value = YamlHelper.Deserializer.Deserialize(reader, type);
                MessagePackSerializer.Serialize(type, ms, value, MessagePackHelper.Options);
            }

            ms.Seek(0, SeekOrigin.Begin);

            using UserDataBatchContext batch = OpenBatch();
            string table = type != null ? GetDataTable(type, true) : GetDataTable(name, true);
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadWrite,
                EnsureExistsQuery + @$"
                    REPLACE INTO [{table}] (uid, format, value)
                    VALUES ($uid, $format, zeroblob($length));

                    SELECT _ROWID_
                    FROM [{table}]
                    WHERE uid = $uid;
                ",
                { "$uid", uid },
                { "$format", (int) format },
                { "$length", ms.Length },
            };
            (SqliteConnection con, SqliteCommand cmd) = mini.Run(out long? rowid);

            if (rowid == null) {
                Logger.Log(LogLevel.ERR, "sqlite", $"InsertData: Failed getting rowid for {table} of UID {uid}");
                return;
            }

            using SqliteBlob blob = new(con, table, "value", (long) rowid);
            ms.CopyTo(blob);
            blob.Dispose();
        }

        public override void InsertFile(string uid, string name, Stream stream) {
            using MemoryStream ms = new();
            stream.CopyTo(ms);

            ms.Seek(0, SeekOrigin.Begin);

            using UserDataBatchContext batch = OpenBatch();
            string table = GetFileTable(name, true);
            using MiniCommand mini = new(this) {
                SqliteOpenMode.ReadWrite,
                EnsureExistsQuery + @$"
                    REPLACE INTO [{table}] (uid, value)
                    VALUES ($uid, zeroblob($length));

                    SELECT _ROWID_
                    FROM [{table}]
                    WHERE uid = $uid;
                ",
                { "$uid", uid },
                { "$length", ms.Length },
            };
            (SqliteConnection con, SqliteCommand cmd) = mini.Run(out long? rowid);

            if (rowid == null) {
                Logger.Log(LogLevel.ERR, "sqlite", $"InsertFile: Failed getting rowid for {table} of UID {uid}");
                return;
            }

            using SqliteBlob blob = new(con, table, "value", (long) rowid);
            ms.CopyTo(blob);
            blob.Dispose();
        }

        public void RunSelfTests() {

            int tests = 0, warnings = 0;

            #region Test Expectations

            var expected = new List<List<string>>() {
                new List<string>() { "123" },
                new List<string>() { "123", "124" },
                new List<string>() { "124" },
            };

            var holdTestUserData = new List<Dictionary<string, string>>();

            bool checkExpected(int idx, string[] uids, string test) {
                bool res = true;
                var ex = expected[idx];

                if (ex.Count != uids.Length)
                    res = false;

                foreach (string u in uids) {
                    if (!ex.Contains(u))
                        res = false;
                }

                if (!res)
                    log($"{test} did not match expected {l2s(ex)}", LogLevel.WRN);

                return res;
            }

            int recordTestUserData() {
                holdTestUserData.Add(new Dictionary<string, string>());

                var dict = holdTestUserData[^1];

                foreach (string uid in GetAll()) {
                    dict.Add(uid, GetKey(uid));
                }

                return holdTestUserData.Count - 1;
            }

            void compareTestUserData(int idx, Dictionary<string, bool> shouldEqual) {
                var dict = holdTestUserData[idx];

                foreach (string uid in shouldEqual.Keys) {
                    if (!dict.ContainsKey(uid)) {
                        log($"Unknown uid {uid} that didn't exist in {a2s(dict.Keys.ToArray())}", LogLevel.WRN);
                        continue;
                    }

                    var key = GetKey(uid);
                    if (key.Equals(dict[uid]) != shouldEqual[uid]) {
                        if (shouldEqual[uid])
                            log($"Key {key} of {uid} should've equaled {dict[uid]} but didn't", LogLevel.WRN);
                        else
                            log($"Key {key} of {uid} should've changed from {dict[uid]} but didn't", LogLevel.WRN);
                    }
                }
            }

            BanInfo biExample = new BanInfo {
                Name = "the name",
                UID = "the uid",
                From = new DateTime(2024, 03, 23, 13, 37, 42, 666, DateTimeKind.Utc),
                To = DateTime.UtcNow
            };

            bool compareBanInfo(BanInfo got) {
                if (got == null) return false;

                if (got.Name != biExample.Name) return false;
                if (got.UID != biExample.UID) return false;

                if (got.From == null || biExample.From == null) return false;

                var c = DateTime.Compare(got.From.Value, biExample.From.Value);
                if (c != 0) {
                    log($"From dates {got.From.Value} {(c > 0 ? ">" : "<")} {biExample.From.Value}");
                    return false;
                }
                if (got.To == null || biExample.To == null) return false;
                c = DateTime.Compare(got.To.Value, biExample.To.Value);
                if (c != 0) {
                    log($"To dates {got.To.Value} {(c > 0 ? ">" : "<")} {biExample.To.Value}");
                    return false;
                }

                return true;
            }

            string testContent = @"
            This is test content
            And it has multiple lines
            very cool
            ";

            #endregion

            #region Test logging

            void log(string msg, LogLevel ll = LogLevel.INF) {
                Logger.Log(ll, "sqlite", msg);
                if (ll > LogLevel.INF)
                    warnings++;
            }

            void logTest(string title) => log($"\n === Test {++tests}: {title}");

            void logKeyUID(string uid) {
                log($"{uid} => {GetKey(uid)} => {GetUID(GetKey(uid))}");
            }

            static string a2s(string[] a) => "[ " + string.Join(", ", a) + " ]";
            static string l2s(List<string> l) => "[ " + string.Join(", ", l) + " ]";

            void logCurrentState() {

                log($"Counts: {GetRegisteredCount()} / {GetAllCount()}");
                log($"Registered: {a2s(GetRegistered())}");
                log($"All: {a2s(GetAll())}");

                log("");
            }

            #endregion

            if (GetAllCount() > 0) {
                log("Aborting self tests because meta is not empty.", LogLevel.WRN);
                return;
            }

            log($"All Tables:\n\t{string.Join(", ", GetAllTables())}");

            log($"AllCount: {GetAllCount()}");
            log( $"RegisteredCount: {GetRegisteredCount()}");

            #region UID / Key Tests

            logTest("Create 123 false =====================");
            Create("123", false);
            logKeyUID("123");

            logCurrentState();

            checkExpected(0, GetAll(), "GetAll()");
            checkExpected(0, GetRegistered(), "GetRegistered()");
            var test1 = recordTestUserData();

            logTest("Create 123 false =====================");
            Create("123", false);
            logKeyUID("123");

            logCurrentState();

            checkExpected(0, GetAll(), "GetAll()");
            checkExpected(0, GetRegistered(), "GetRegistered()");
            compareTestUserData(test1, new Dictionary<string, bool> { { "123", true } });

            logTest("Create 123 true =====================");
            Create("123", true);
            logKeyUID("123");

            logCurrentState();

            checkExpected(0, GetAll(), "GetAll()");
            checkExpected(0, GetRegistered(), "GetRegistered()");
            compareTestUserData(test1, new Dictionary<string, bool> { { "123", false } });

            var test2 = recordTestUserData();

            logTest("Create 124 true =====================");
            Create("124", true);
            logKeyUID("123");
            logKeyUID("124");

            logCurrentState();

            checkExpected(1, GetAll(), "GetAll()");
            checkExpected(1, GetRegistered(), "GetRegistered()");
            compareTestUserData(test1, new Dictionary<string, bool> { { "123", false } });
            compareTestUserData(test2, new Dictionary<string, bool> { { "123", true } });

            var test3 = recordTestUserData();

            logTest("Revoke 123's key =====================");
            RevokeKey(GetKey("123"));
            logKeyUID("123");
            logKeyUID("124");

            logCurrentState();

            // I thought revoke deletes the UID entirely like I think it does in FileSystemUserData, but guess it doesn't
            checkExpected(1, GetAll(), "GetAll()");
            checkExpected(1, GetRegistered(), "GetRegistered()");
            compareTestUserData(test3, new Dictionary<string, bool> { { "123", false }, { "124", true } });

            logTest("Create 123 false =====================");
            Create("123", false);
            logKeyUID("123");

            logCurrentState();

            checkExpected(1, GetAll(), "GetAll()");
            checkExpected(1, GetRegistered(), "GetRegistered()");
            compareTestUserData(test1, new Dictionary<string, bool> { { "123", false } });
            compareTestUserData(test2, new Dictionary<string, bool> { { "123", false } });
            compareTestUserData(test3, new Dictionary<string, bool> { { "123", false }, { "124", true } });

            #endregion

            #region Generics Data Tests

            BanInfo bi;

            logTest("TryLoad 123 BanInfo =====================");

            if (TryLoad("123", out bi))
                log($"Expected to fail TryLoad but got BanInfo {bi}", LogLevel.WRN);

            logTest("Save 123 BanInfo =====================");

            Save("123", biExample);

            logTest("TryLoad 123 BanInfo =====================");

            if (TryLoad("123", out bi)) {
                if (!compareBanInfo(bi))
                    log($"BanInfo {bi} did not match input {biExample}", LogLevel.WRN);
            } else
                log("Expected to TryLoad but got no BanInfo", LogLevel.WRN);

            logTest("Delete 123 BanInfo =====================");

            Delete<BanInfo>("123");

            logTest("TryLoad 123 BanInfo =====================");

            if (TryLoad("123", out bi))
                log($"Expected to fail TryLoad but got BanInfo {bi}", LogLevel.WRN);

            #endregion

            #region File Data Tests

            logTest("HasFile 123 test =====================");

            if (HasFile("123", "test"))
                log("Expected to fail HasFile", LogLevel.WRN);

            logTest("WriteFile 123 test =====================");

            using (var stream = WriteFile("123", "test")) {
                if (stream == null || !stream.CanWrite) {
                    log("WriteFile didn't open a stream or cannot write", LogLevel.WRN);
                } else {
                    stream.Write(Encoding.UTF8.GetBytes(testContent));
                }
            }

            logTest("HasFile 123 test =====================");

            if (!HasFile("123", "test"))
                log("Expected to succeed HasFile", LogLevel.WRN);

            logTest("ReadFile 123 test =====================");

            using (var stream = ReadFile("123", "test")) {
                if (stream == null || !stream.CanRead) {
                    log("ReadFile didn't open a stream or cannot read", LogLevel.WRN);
                } else {
                    byte[] buf = new byte[testContent.Length + 5];
                    var read = stream.Read(buf, 0, buf.Length);
                    if (read == -1)
                        log("ReadFile read returned -1", LogLevel.WRN);
                    else if (read != testContent.Length)
                        log($"ReadFile read {read} bytes instead of testContent.Length == {testContent.Length}", LogLevel.WRN);
                    else if (Encoding.UTF8.GetString(buf, 0, read) != testContent)
                        log("ReadFile read UTF8 didn't match testContent", LogLevel.WRN);
                }
            }

            logTest("DeleteFile 123 test =====================");

            DeleteFile("123", "test");

            logTest("HasFile 123 test =====================");

            if (HasFile("123", "test"))
                log("Expected to fail HasFile", LogLevel.WRN);

            #endregion

            log($" === {tests} tests done, {warnings} warnings logged. === ", warnings > 0 ? LogLevel.WRN : LogLevel.INF);
        }

        public struct MiniCommand : IDisposable, IEnumerable<(string, object)> {
            public SqliteUserData UserData;
            public string CommandText;
            public List<(string, object)>? Parameters;
            public SqliteOpenMode PreferredMode;

            public BatchContext? Batch;

            public SqliteOpenMode CurrentMode;
            public SqliteConnection? Connection;
            public SqliteCommand? Command;
            public SqliteDataReader? Reader;

            public MiniCommand(SqliteUserData userData) {
                UserData = userData;
                CommandText = "";
                Parameters = default;
                PreferredMode = default;
                Batch = default;
                CurrentMode = default;
                Connection = default;
                Command = default;
                Reader = default;
            }

            public void Next(MiniCommand cmd) {
                CommandText = cmd.CommandText;
                Parameters = cmd.Parameters;
                PreferredMode = cmd.PreferredMode;
            }

            public void Add(SqliteOpenMode mode) => PreferredMode = mode;
            public void Add(SqliteConnection? connection) => Connection = connection;
            public void Add(string text) => CommandText = text;
            public void Add(string key, object value) => (Parameters ??= new()).Add((key, value));

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerator<(string, object)> GetEnumerator() {
                yield return ("", CommandText);
                if (Parameters != null)
                    foreach ((string, object) param in Parameters)
                        yield return param;
            }

            public (SqliteConnection, SqliteCommand) Prepare() {
                if (Batch == null) {
                    BatchContext batch = UserData.Batch;
                    if (batch.Count > 0)
                        Batch = batch;
                }

                Reader?.Close();
                ((IDisposable?) Reader)?.Dispose();
                Reader = null;

                if (CurrentMode > PreferredMode) {
                    Command?.Dispose();
                    Command = null;
                    Connection?.Dispose();
                    Connection = null;
                    if (Batch != null) {
                        Batch.Command = null;
                        Batch.Connection = null;
                    }
                }

                SqliteConnection con = Connection ??= (Batch?.Open(PreferredMode) ?? UserData.Open(PreferredMode));

                SqliteCommand cmd = Command ??= (Batch?.Command ?? con.CreateCommand());
                cmd.CommandText = CommandText;
                if (Parameters != null) {
                    cmd.Parameters.Clear();
                    foreach ((string Key, object Value) param in Parameters)
                        cmd.Parameters.AddWithValue(param.Key, param.Value);
                }

                return (con, cmd);
            }

            public (SqliteConnection, SqliteCommand, SqliteDataReader) Read() {
                (SqliteConnection con, SqliteCommand cmd) = Prepare();
                SqliteDataReader reader = Reader = cmd.ExecuteReader();
                return (con, cmd, reader);
            }

            public (SqliteConnection con, SqliteCommand cmd, int value) Run() {
                (SqliteConnection con, SqliteCommand cmd) = Prepare();
                return (con, cmd, cmd.ExecuteNonQuery());
            }

            public (SqliteConnection con, SqliteCommand cmd, T? value) Run<T>() {
                (SqliteConnection con, SqliteCommand cmd) = Prepare();
                return (con, cmd, (T?) cmd.ExecuteScalar());
            }

            public (SqliteConnection con, SqliteCommand cmd) Run<T>(out T? value) {
                (SqliteConnection con, SqliteCommand cmd) = Prepare();
                value = (T?) cmd.ExecuteScalar();
                return (con, cmd);
            }

            public void Dispose() {
                Reader?.Close();
                ((IDisposable?) Reader)?.Dispose();
                Reader = null;
                if (Batch == null) {
                    Command?.Dispose();
                    Command = null;
                    Connection?.Dispose();
                    Connection = null;
                }
            }
        }

        public enum DataFormat {
            MessagePack,
            Yaml
        }

        public sealed class BatchContext : UserDataBatchContext {

            public SqliteUserData UserData;
            public SqliteOpenMode Mode;
            public SqliteConnection? Connection;
            public SqliteCommand? Command;
            public SqliteTransaction? Transaction;

            public int Count;

            internal BatchContext(SqliteUserData userData) {
                UserData = userData;
            }

            [MemberNotNull(nameof(Connection))]
            [MemberNotNull(nameof(Command))]
            public SqliteConnection Open(SqliteOpenMode mode) {
                if (Connection != null && Command != null && Mode <= mode)
                    return Connection;

                if (Connection != null) {
                    Transaction?.Commit();
                    Transaction?.Dispose();
                    Transaction = null;
                    Command?.Dispose();
                    Command = null;
                    Connection?.Dispose();
                    Connection = null;
                }

                Mode = mode;
                Connection = UserData.Open(mode);
                Command = Connection.CreateCommand();
                if (Mode <= SqliteOpenMode.ReadWrite) {
                    Transaction = Connection.BeginTransaction(true);
                    Command.Transaction = Transaction;
                }
                return Connection;
            }

            public override void Dispose() {
                Count--;
                if (Count <= 0) {
                    Transaction?.Commit();
                    Transaction?.Dispose();
                    Transaction = null;
                    Command?.Dispose();
                    Command = null;
                    Connection?.Dispose();
                    Connection = null;
                }
            }

        }

    }
}
