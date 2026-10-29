
namespace ClientShowcase.Data;

public sealed class SettingsRepository(Database db)
{
    private SiteSettings? _cache;
    private readonly Lock _gate = new();

    public SiteSettings Get()
    {
        lock (_gate)
        {
            if (_cache is not null) return _cache;
            var settings = new SiteSettings();
            using var conn = db.Open();
            using var cmd = conn.Command("SELECT key, value FROM settings");
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var value = r.GetString(1);
                switch (r.GetString(0))
                {
                    case nameof(SiteSettings.SiteName): settings.SiteName = value; break;
                    case nameof(SiteSettings.Tagline): settings.Tagline = value; break;
                    case nameof(SiteSettings.HeroTitle): settings.HeroTitle = value; break;
                    case nameof(SiteSettings.HeroSubtitle): settings.HeroSubtitle = value; break;
                    case nameof(SiteSettings.FooterNote): settings.FooterNote = value; break;
                }
            }
            return _cache = settings;
        }
    }

    public void Save(SiteSettings settings)
    {
        lock (_gate)
        {
            using var conn = db.Open();
            using var tx = conn.BeginTransaction();
            foreach (var (key, value) in new Dictionary<string, string>
            {
                [nameof(SiteSettings.SiteName)] = settings.SiteName,
                [nameof(SiteSettings.Tagline)] = settings.Tagline,
                [nameof(SiteSettings.HeroTitle)] = settings.HeroTitle,
                [nameof(SiteSettings.HeroSubtitle)] = settings.HeroSubtitle,
                [nameof(SiteSettings.FooterNote)] = settings.FooterNote,
            })
            {
                // Bypass the empty-string-to-NULL conversion: settings are allowed to be blank.
                var cmd = conn.Command("INSERT INTO settings (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value = @v", tx);
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", value);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            _cache = null;
        }
    }
}

public sealed class AdminRepository(Database db)
{
    public bool Any()
    {
        using var conn = db.Open();
        return conn.Command("SELECT 1 FROM admins LIMIT 1").ExecuteScalar() is not null;
    }

    public Admin? Find(string username)
    {
        using var conn = db.Open();
        using var cmd = conn.Command("SELECT * FROM admins WHERE username = @u").With("@u", username);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public Admin? FindById(long id)
    {
        using var conn = db.Open();
        using var cmd = conn.Command("SELECT * FROM admins WHERE id = @id").With("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public long Create(string username, string passwordHash)
    {
        using var conn = db.Open();
        return (long)conn.Command("""
            INSERT INTO admins (username, password_hash, security_stamp, created_at) VALUES (@u, @h, @s, @now);
            SELECT last_insert_rowid();
            """).With("@u", username).With("@h", passwordHash).With("@s", Guid.NewGuid().ToString("N"))
            .With("@now", DateTime.UtcNow).ExecuteScalar()!;
    }

    /// <summary>Changing the password rotates the security stamp, which signs out every other session.</summary>
    public string UpdatePassword(long id, string passwordHash)
    {
        var stamp = Guid.NewGuid().ToString("N");
        using var conn = db.Open();
        conn.Command("UPDATE admins SET password_hash = @h, security_stamp = @s WHERE id = @id")
            .With("@h", passwordHash).With("@s", stamp).With("@id", id).ExecuteNonQuery();
        return stamp;
    }

    public void RecordFailure(long id, int failedLogins, DateTime? lockedUntil)
    {
        using var conn = db.Open();
        conn.Command("UPDATE admins SET failed_logins = @f, locked_until = @l WHERE id = @id")
            .With("@f", failedLogins).With("@l", lockedUntil).With("@id", id).ExecuteNonQuery();
    }

    public void ResetFailures(long id) => RecordFailure(id, 0, null);

    private static Admin Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        Username = r.Str("username")!,
        PasswordHash = r.Str("password_hash")!,
        SecurityStamp = r.Str("security_stamp")!,
        FailedLogins = r.Int("failed_logins"),
        LockedUntil = r.NullableDate("locked_until"),
    };
}
