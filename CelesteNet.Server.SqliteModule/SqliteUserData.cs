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

                        CREATE TABLE meta (
                            iid INTEGER PRIMARY KEY AUTOINCREMENT,
                            uid VARCHAR(255) UNIQUE,
                            key VARCHAR(255),
                            keyfull VARCHAR(255),
                            registered BOOLEAN
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

        public string GetBlobTable(string name) {
            // FIXME: AAAAAAAAAAAAAAAAAAAAAAAAAA
            name = name.Sanitize(Illegal);
            using MiniCommand mini = new(this) {
                @$"
                    CREATE TABLE IF NOT EXISTS [{name}] (
                        iid INTEGER PRIMARY KEY AUTOINCREMENT,
                        uid VARCHAR(255) UNIQUE,
                        value BLOB
                    );

                    SELECT name
                    FROM sqlite_master
                    WHERE type = 'table'
                    AND name = $name;
                ",
                { "$name", name },
            };
            return mini.Run<string>().value;
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

        public string GetDataTable(Type? type)
            => GetBlobTable($"data.{type?.FullName ?? "unknown"}");

        public string GetFileTable(string name)
            => GetBlobTable($"file.{name}");

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
                    SELECT value
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

            using Stream stream = reader.GetStream(0);
            value = MessagePackSerializer.Deserialize<T>(stream, MessagePackHelper.Options) ?? new();
            return true;
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
            MessagePackSerializer.Serialize(ms, value, MessagePackHelper.Options);

            ms.Seek(0, SeekOrigin.Begin);

            string table = GetDataTable(typeof(T));
            using MiniCommand mini = new(this) {
                @$"
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

        public override Stream WriteFile(string uid, string name) {
            MemoryStream ms = new();
            return new DisposeActionStream(ms, () => {
                ms.Seek(0, SeekOrigin.Begin);

                string table = GetFileTable(name);
                using MiniCommand mini = new(this) {
                    @$"
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
                    SELECT D.value
                    FROM [{GetDataTable(typeof(T))}] D
                    INNER JOIN meta M ON D.uid = M.uid
                    WHERE M.registered = 1;
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            List<T> values = new();
            while (reader.Read()) {
                using Stream stream = reader.GetStream(0);
                values.Add(MessagePackSerializer.Deserialize<T>(stream, MessagePackHelper.Options) ?? new());
            }

            return values.ToArray();
        }

        public override T[] LoadAll<T>() {
            using MiniCommand mini = new(this) {
                @$"
                    SELECT value
                    FROM [{GetDataTable(typeof(T))}];
                ",
            };
            (SqliteConnection con, SqliteCommand cmd, SqliteDataReader reader) = mini.Read();

            List<T> values = new();
            while (reader.Read()) {
                using Stream stream = reader.GetStream(0);
                values.Add(MessagePackSerializer.Deserialize<T>(stream, MessagePackHelper.Options) ?? new());
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

    }
}
