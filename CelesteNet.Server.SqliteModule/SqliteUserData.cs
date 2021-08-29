using MessagePack;
using Microsoft.Data.Sqlite;
using Microsoft.Xna.Framework;
using Monocle;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace Celeste.Mod.CelesteNet.Server.Sqlite {
    public class SqliteUserData : UserData {

        public static readonly HashSet<char> Illegal = new("`´'\"^[]\\//");

        public readonly object GlobalLock = new();

        public readonly SqliteModule Module;

        public string UserRoot => Path.Combine(Module.Settings.UserDataRoot, "User");
        public string DBPath => Path.Combine(Module.Settings.UserDataRoot, "main.db");

        public SqliteUserData(SqliteModule module)
            : base(module.Server) {
            Module = module;

            if (!File.Exists(DBPath)) {
                using MiniCommand mini = new(this) {
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

        public SqliteConnection Open() {
            SqliteConnection con = new(new SqliteConnectionStringBuilder() {
                DataSource = DBPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString());
            con.Open();
            return con;
        }

        public List<string> GetAllTables() {
            using MiniCommand mini = new(this) {
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

        public string GetDataTable(Type type) {
            type ??= typeof(object);
            string real = type.FullName ?? throw new Exception($"Type without full name: {type}");
            string typename = type.AssemblyQualifiedName ?? throw new Exception($"Type without assembly qualified name: {type}");

            // FIXME: AAAAAAAAAAAAAAAAAAAAAAAAAA
            string name = $"data.{real}".Sanitize(Illegal);
            using MiniCommand mini = new(this) {
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
            return mini.Run<string>().value;
        }

        public string GetDataTable(string shortname) {
            // FIXME: AAAAAAAAAAAAAAAAAAAAAAAAAA
            string name = $"data.{shortname}".Sanitize(Illegal);
            using MiniCommand mini = new(this) {
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
            return mini.Run<string>().value;
        }

        public string GetFileTable(string name) {
            string real = name;

            // FIXME: AAAAAAAAAAAAAAAAAAAAAAAAAA
            name = $"file.{real}".Sanitize(Illegal);
            using MiniCommand mini = new(this) {
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
            return mini.Run<string>().value;
        }

        public override string GetUID(string key) {
            using MiniCommand mini = new(this) {
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
            using MiniCommand mini = new(this) {
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
            using MiniCommand mini = new(this) {
                @$"
                    SELECT format, value
                    FROM [{GetDataTable(typeof(T))}]
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

            switch ((DataFormat) reader.GetInt32(0)) {
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
        }

        public override Stream? ReadFile(string uid, string name) {
            using MiniCommand mini = new(this) {
                @$"
                    SELECT value
                    FROM [{GetFileTable(name)}]
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

            ms = new MemoryStream();
            using (stream)
                stream.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        public override void Save<T>(string uid, T value) {
            using MemoryStream ms = new();
            MessagePackSerializer.Serialize(typeof(T), ms, value, MessagePackHelper.Options);

            ms.Seek(0, SeekOrigin.Begin);

            string table = GetDataTable(typeof(T));
            using MiniCommand mini = new(this) {
                EnsureExistsQuery + @$"
                    REPLACE INTO [{table}] (uid, format, value)
                    VALUES ($uid, $format, zeroblob($length));

                    SELECT last_insert_rowid();
                ",
                { "$uid", uid },
                { "$format", (int) DataFormat.MessagePack },
                { "$length", ms.Length },
            };
            (SqliteConnection con, SqliteCommand cmd) = mini.Run(out long rowid);

            using SqliteBlob blob = new(con, table, "value", rowid);
            ms.CopyTo(blob);
        }

        public override Stream WriteFile(string uid, string name) {
            MemoryStream ms = new();
            return new DisposeActionStream(ms, () => {
                ms.Seek(0, SeekOrigin.Begin);

                string table = GetFileTable(name);
                using MiniCommand mini = new(this) {
                    EnsureExistsQuery + @$"
                        REPLACE INTO [{table}] (uid, value)
                        VALUES ($uid, zeroblob($length));

                        SELECT last_insert_rowid();
                    ",
                    { "$uid", uid },
                    { "$length", ms.Length },
                };
                (SqliteConnection con, SqliteCommand cmd) = mini.Run(out long rowid);

                using SqliteBlob blob = new(con, table, "value", rowid);
                ms.CopyTo(blob);
            });
        }

        public override void Delete<T>(string uid) {
            using MiniCommand mini = new(this) {
                @$"
                    DELETE
                    FROM [{GetDataTable(typeof(T))}]
                    WHERE uid = $uid;
                ",
                { "$uid", uid },
            };
            mini.Run();

            CheckCleanup(uid);
        }

        public override void DeleteFile(string uid, string name) {
            using MiniCommand mini = new(this) {
                @$"
                    DELETE
                    FROM [{GetFileTable(name)}]
                    WHERE uid = $uid;
                ",
                { "$uid", uid },
            };
            mini.Run();

            CheckCleanup(uid);
        }

        private void CheckCleanup(string uid) {
            using MiniCommand mini = new(this);

            mini.Next(new() {
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
            using MiniCommand mini = new(this) {
                string.Join('\n', GetAllTables().Select(table => $@"
                    DELETE
                    FROM [{table}]
                    WHERE uid = $uid;
                ")),
                { "$uid", uid },
            };
            mini.Run();
        }

        public override T[] LoadRegistered<T>() {
            using MiniCommand mini = new(this) {
                @$"
                    SELECT D.format, D.value
                    FROM [{GetDataTable(typeof(T))}] D
                    INNER JOIN meta M ON D.uid = M.uid
                    WHERE M.registered = 1;
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            List<T> values = new();
            while (reader.Read()) {
                switch ((DataFormat) reader.GetInt32(0)) {
                    case DataFormat.MessagePack:
                    default: {
                        using Stream stream = reader.GetStream(1);
                        values.Add(MessagePackSerializer.Deserialize<T>(stream, MessagePackHelper.Options) ?? new());
                        break;
                    }

                    case DataFormat.Yaml: {
                        using Stream stream = reader.GetStream(1);
                        using StreamReader streamReader = new(stream);
                        values.Add(YamlHelper.Deserializer.Deserialize<T>(streamReader) ?? new());
                        break;
                    }
                }
            }

            return values.ToArray();
        }

        public override T[] LoadAll<T>() {
            using MiniCommand mini = new(this) {
                @$"
                    SELECT format, value
                    FROM [{GetDataTable(typeof(T))}];
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            List<T> values = new();
            while (reader.Read()) {
                switch ((DataFormat) reader.GetInt32(0)) {
                    case DataFormat.MessagePack:
                    default: {
                        using Stream stream = reader.GetStream(1);
                        values.Add(MessagePackSerializer.Deserialize<T>(stream, MessagePackHelper.Options) ?? new());
                        break;
                    }

                    case DataFormat.Yaml: {
                        using Stream stream = reader.GetStream(1);
                        using StreamReader streamReader = new(stream);
                        values.Add(YamlHelper.Deserializer.Deserialize<T>(streamReader) ?? new());
                        break;
                    }
                }
            }

            return values.ToArray();
        }

        public override string[] GetRegistered() {
            using MiniCommand mini = new(this) {
                @"
                    SELECT uid
                    FROM meta
                    WHERE registered = 1;
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();
            List<string> uids = new();
            while (reader.Read())
                uids.Add(reader.GetString(0));
            return uids.ToArray();
        }

        public override string[] GetAll() {
            using MiniCommand mini = new(this) {
                @"
                    SELECT uid
                    FROM meta;
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();
            List<string> uids = new();
            while (reader.Read())
                uids.Add(reader.GetString(0));
            return uids.ToArray();
        }

        public override int GetRegisteredCount() {
            using MiniCommand mini = new(this) {
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

        public override string Create(string uid) {
            lock (GlobalLock) {
                string key = GetKey(uid);
                if (!key.IsNullOrEmpty())
                    return key;

                string keyFull;
                do {
                    keyFull = Guid.NewGuid().ToString().Replace("-", "");
                    key = keyFull.Substring(0, 16);
                } while (!GetUID(key).IsNullOrEmpty());

                using MiniCommand mini = new(this) {
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
            lock (GlobalLock) {
                MiniCommand mini = new(this);

                mini.Next(new() {
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
                        $@"
                            SELECT uid, value
                            FROM [{table}];
                        ",
                    });
                    (con, cmd, reader) = mini.Read();

                    if (data) {
                        using MiniCommand miniName = new(this) {
                            mini.Connection,
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

            string table = type != null ? GetDataTable(type) : GetDataTable(name);
            using MiniCommand mini = new(this) {
                EnsureExistsQuery + @$"
                    REPLACE INTO [{table}] (uid, format, value)
                    VALUES ($uid, $format, zeroblob($length));

                    SELECT last_insert_rowid();
                ",
                { "$uid", uid },
                { "$format", (int) format },
                { "$length", ms.Length },
            };
            (SqliteConnection con, SqliteCommand cmd) = mini.Run(out long rowid);

            using SqliteBlob blob = new(con, table, "value", rowid);
            ms.CopyTo(blob);
        }

        public override void InsertFile(string uid, string name, Stream stream) {
            using MemoryStream ms = new();
            stream.CopyTo(ms);

            ms.Seek(0, SeekOrigin.Begin);

            string table = GetFileTable(name);
            using MiniCommand mini = new(this) {
                EnsureExistsQuery + @$"
                    REPLACE INTO [{table}] (uid, value)
                    VALUES ($uid, zeroblob($length));

                    SELECT last_insert_rowid();
                ",
                { "$uid", uid },
                { "$length", ms.Length },
            };
            (SqliteConnection con, SqliteCommand cmd) = mini.Run(out long rowid);

            using SqliteBlob blob = new(con, table, "value", rowid);
            ms.CopyTo(blob);
        }

        public struct MiniCommand : IDisposable, IEnumerable<(string, object)> {
            public SqliteUserData UserData;
            public string CommandText;
            public List<(string, object)>? Parameters;

            public SqliteConnection? Connection;
            public SqliteCommand? Command;
            public SqliteDataReader? Reader;

            public MiniCommand(SqliteUserData userData) {
                UserData = userData;
                CommandText = "";
                Parameters = null;
                Connection = null;
                Command = null;
                Reader = null;
            }

            public MiniCommand(SqliteUserData userData, SqliteConnection connection, SqliteCommand command) {
                UserData = userData;
                CommandText = "";
                Parameters = null;
                Connection = connection;
                Command = command;
                Reader = null;
            }

            public void Next(MiniCommand cmd) {
                ((IDisposable?) Reader)?.Dispose();
                Reader = null;
                // Command?.Dispose();
                // Command = null;

                CommandText = cmd.CommandText;
                Parameters = cmd.Parameters;
            }

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
                if (Connection != null && Command != null)
                    return (Connection, Command);

                SqliteConnection con = Connection ??= UserData.Open();

                SqliteCommand cmd = Command ??= con.CreateCommand();
                cmd.CommandText = CommandText;
                if (Parameters != null)
                    foreach ((string Key, object Value) param in Parameters)
                        cmd.Parameters.AddWithValue(param.Key, param.Value);

                return (con, cmd);
            }

            public (SqliteConnection, SqliteCommand, SqliteDataReader) Read() {
                if (Connection != null && Command != null && Reader != null)
                    return (Connection, Command, Reader);

                (SqliteConnection con, SqliteCommand cmd) = Prepare();
                SqliteDataReader reader = Reader = cmd.ExecuteReader();
                return (con, cmd, reader);
            }

            public (SqliteConnection con, SqliteCommand cmd, int value) Run() {
                (SqliteConnection con, SqliteCommand cmd) = Prepare();
                return (con, cmd, cmd.ExecuteNonQuery());
            }

            public (SqliteConnection con, SqliteCommand cmd, T value) Run<T>() {
                (SqliteConnection con, SqliteCommand cmd) = Prepare();
                return (con, cmd, (T) cmd.ExecuteScalar());
            }

            public (SqliteConnection con, SqliteCommand cmd) Run<T>(out T value) {
                (SqliteConnection con, SqliteCommand cmd) = Prepare();
                value = (T) cmd.ExecuteScalar();
                return (con, cmd);
            }

            public void Dispose() {
                ((IDisposable?) Reader)?.Dispose();
                Reader = null;
                Command?.Dispose();
                Command = null;
                Connection?.Dispose();
                Connection = null;
            }
        }

        public enum DataFormat {
            MessagePack,
            Yaml
        }

    }
}
