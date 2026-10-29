using Microsoft.Data.Sqlite;

namespace ClientShowcase.Data;

/// <summary>
/// Opens SQLite connections and keeps the schema up to date.
/// All data (clients, photos, rankings, work history, settings, admins) lives in one file.
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(StoragePaths paths)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
            DefaultTimeout = 30,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public void Migrate()
    {
        using var connection = Open();
        using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode = WAL;";
            wal.ExecuteNonQuery();
        }

        long version;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            version = (long)cmd.ExecuteScalar()!;
        }

        if (version < 1)
        {
            using var tx = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE admins (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    username      TEXT NOT NULL UNIQUE COLLATE NOCASE,
                    password_hash TEXT NOT NULL,
                    security_stamp TEXT NOT NULL,
                    failed_logins INTEGER NOT NULL DEFAULT 0,
                    locked_until  TEXT NULL,
                    created_at    TEXT NOT NULL
                );

                CREATE TABLE clients (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    slug          TEXT NOT NULL UNIQUE,
                    full_name     TEXT NOT NULL,
                    headline      TEXT NULL,
                    age           INTEGER NULL,
                    location      TEXT NULL,
                    bio           TEXT NULL,
                    phone         TEXT NULL,
                    email         TEXT NULL,
                    tiktok_url    TEXT NULL,
                    instagram_url TEXT NULL,
                    show_phone    INTEGER NOT NULL DEFAULT 1,
                    show_email    INTEGER NOT NULL DEFAULT 1,
                    is_published  INTEGER NOT NULL DEFAULT 1,
                    private_notes TEXT NULL,
                    position      INTEGER NOT NULL,
                    created_at    TEXT NOT NULL,
                    updated_at    TEXT NOT NULL
                );
                CREATE INDEX ix_clients_position ON clients(position);

                CREATE TABLE client_photos (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    client_id  INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                    file       TEXT NOT NULL,
                    thumb      TEXT NOT NULL,
                    width      INTEGER NOT NULL DEFAULT 0,
                    height     INTEGER NOT NULL DEFAULT 0,
                    position   INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX ix_client_photos_client ON client_photos(client_id, position);

                CREATE TABLE work_entries (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    client_id   INTEGER NOT NULL REFERENCES clients(id) ON DELETE CASCADE,
                    title       TEXT NOT NULL,
                    service     TEXT NULL,
                    description TEXT NULL,
                    work_date   TEXT NULL,
                    links_json  TEXT NOT NULL DEFAULT '[]',
                    position    INTEGER NOT NULL,
                    created_at  TEXT NOT NULL,
                    updated_at  TEXT NOT NULL
                );
                CREATE INDEX ix_work_entries_client ON work_entries(client_id, position);

                CREATE TABLE work_images (
                    id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    entry_id   INTEGER NOT NULL REFERENCES work_entries(id) ON DELETE CASCADE,
                    file       TEXT NOT NULL,
                    thumb      TEXT NOT NULL,
                    width      INTEGER NOT NULL DEFAULT 0,
                    height     INTEGER NOT NULL DEFAULT 0,
                    position   INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );
                CREATE INDEX ix_work_images_entry ON work_images(entry_id, position);

                CREATE TABLE settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                PRAGMA user_version = 1;
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        if (version < 2)
        {
            // Rebrand: the site is now called "The Rooster"; refresh the default homepage copy too.
            using var tx = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO settings (key, value) VALUES ('SiteName', 'The Rooster')
                    ON CONFLICT(key) DO UPDATE SET value = 'The Rooster';
                DELETE FROM settings WHERE key IN ('Tagline', 'HeroTitle', 'HeroSubtitle')
                    AND value IN ('Talent, curated.', 'The people behind our finest work.',
                                  'A curated roster of the creators, faces and collaborators we are proud to work with — each one hand-placed.');
                PRAGMA user_version = 2;
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }

        if (version < 3)
        {
            // Rename to "The Roster" and add priority tiers. Every existing client starts as
            // "Standard", so the current manual order is preserved exactly.
            using var tx = connection.BeginTransaction();
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO settings (key, value) VALUES ('SiteName', 'The Roster')
                    ON CONFLICT(key) DO UPDATE SET value = 'The Roster';
                ALTER TABLE clients ADD COLUMN priority INTEGER NOT NULL DEFAULT 3;
                CREATE INDEX ix_clients_priority ON clients(priority, position);
                PRAGMA user_version = 3;
                """;
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }
}

public sealed class StoragePaths
{
    public StoragePaths(string dataRoot)
    {
        DataRoot = dataRoot;
        MediaRoot = Path.Combine(dataRoot, "media");
        KeysRoot = Path.Combine(dataRoot, "keys");
        DatabaseFile = Path.Combine(dataRoot, "showcase.db");
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(MediaRoot);
        Directory.CreateDirectory(KeysRoot);
    }

    public string DataRoot { get; }
    public string MediaRoot { get; }
    public string KeysRoot { get; }
    public string DatabaseFile { get; }
}
