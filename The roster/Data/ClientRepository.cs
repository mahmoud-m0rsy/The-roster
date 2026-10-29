using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace ClientShowcase.Data;

public sealed record NewImage(string File, string Thumb, int Width, int Height);

public sealed record RankedClient(int Rank, Client Client);

public sealed class ClientRepository(Database db)
{
    private const string ClientColumns = """
        c.id, c.slug, c.full_name, c.headline, c.age, c.location, c.bio, c.phone, c.email,
        c.tiktok_url, c.instagram_url, c.show_phone, c.show_email, c.is_published, c.private_notes,
        c.priority, c.position, c.created_at, c.updated_at,
        p.id AS p_id, p.file AS p_file, p.thumb AS p_thumb, p.width AS p_width, p.height AS p_height,
        (SELECT COUNT(*) FROM client_photos x WHERE x.client_id = c.id) AS photo_count,
        (SELECT COUNT(*) FROM work_entries w WHERE w.client_id = c.id) AS work_count
        """;

    private const string CoverJoin = """
        LEFT JOIN client_photos p ON p.id =
            (SELECT id FROM client_photos WHERE client_id = c.id ORDER BY position, id LIMIT 1)
        """;

    // ───────────────────────────── Public queries ─────────────────────────────

    /// <summary>
    /// Published clients in the exact order assigned by the admin. Rank is the client's
    /// place among published clients and is unaffected by the search filter.
    /// </summary>
    public (List<RankedClient> Items, int Total) ListPublished(string? search, int page, int pageSize)
    {
        using var conn = db.Open();
        var where = string.IsNullOrWhiteSpace(search)
            ? ""
            : @"WHERE r.full_name LIKE @q ESCAPE '\' OR r.headline LIKE @q ESCAPE '\' OR r.location LIKE @q ESCAPE '\'";

        int total;
        using (var count = conn.Command($"""
            SELECT COUNT(*) FROM (SELECT * FROM clients WHERE is_published = 1) r {where}
            """))
        {
            if (where.Length > 0) count.With("@q", Like(search!));
            total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using var cmd = conn.Command($"""
            WITH ranked AS (
                SELECT *, ROW_NUMBER() OVER (ORDER BY priority, position, id) AS rnk
                FROM clients WHERE is_published = 1
            )
            SELECT r.rnk, {ClientColumns}
            FROM ranked r
            JOIN clients c ON c.id = r.id
            {CoverJoin}
            {where}
            ORDER BY r.rnk
            LIMIT @take OFFSET @skip
            """)
            .With("@take", pageSize)
            .With("@skip", (Math.Max(page, 1) - 1) * pageSize);
        if (where.Length > 0) cmd.With("@q", Like(search!));

        var items = new List<RankedClient>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) items.Add(new RankedClient(reader.Int("rnk"), ReadClient(reader)));
        return (items, total);
    }

    public (int Clients, int Projects) PublicStats()
    {
        using var conn = db.Open();
        using var cmd = conn.Command("""
            SELECT (SELECT COUNT(*) FROM clients WHERE is_published = 1),
                   (SELECT COUNT(*) FROM work_entries w JOIN clients c ON c.id = w.client_id WHERE c.is_published = 1)
            """);
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1));
    }

    public (RankedClient? Client, Client? Previous, Client? Next) GetPublishedProfile(string slug)
    {
        using var conn = db.Open();
        using var cmd = conn.Command($"""
            WITH ranked AS (
                SELECT id, ROW_NUMBER() OVER (ORDER BY priority, position, id) AS rnk
                FROM clients WHERE is_published = 1
            )
            SELECT r.rnk, {ClientColumns}
            FROM ranked r JOIN clients c ON c.id = r.id
            {CoverJoin}
            WHERE c.slug = @slug
            """).With("@slug", slug);

        RankedClient? found = null;
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read()) found = new RankedClient(reader.Int("rnk"), ReadClient(reader));
        }
        if (found is null) return (null, null, null);

        LoadDetails(conn, found.Client);
        var previous = Neighbor(conn, found.Client, before: true);
        var next = Neighbor(conn, found.Client, before: false);
        return (found, previous, next);
    }

    private Client? Neighbor(SqliteConnection conn, Client current, bool before)
    {
        var sql = before
            ? $"""
               SELECT {ClientColumns} FROM clients c {CoverJoin}
               WHERE c.is_published = 1 AND (c.priority, c.position, c.id) < (@pr, @pos, @id)
               ORDER BY c.priority DESC, c.position DESC, c.id DESC LIMIT 1
               """
            : $"""
               SELECT {ClientColumns} FROM clients c {CoverJoin}
               WHERE c.is_published = 1 AND (c.priority, c.position, c.id) > (@pr, @pos, @id)
               ORDER BY c.priority, c.position, c.id LIMIT 1
               """;
        using var cmd = conn.Command(sql).With("@pr", current.Priority).With("@pos", current.Position).With("@id", current.Id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadClient(reader) : null;
    }

    /// <summary>
    /// Live-search suggestions among published clients. Names that start with the query come
    /// first, then names containing a word that starts with it, then other matches — each group
    /// in the admin's ranking order.
    /// </summary>
    public List<RankedClient> SearchPublished(string query, int limit)
    {
        using var conn = db.Open();
        using var cmd = conn.Command($"""
            WITH ranked AS (
                SELECT id, ROW_NUMBER() OVER (ORDER BY priority, position, id) AS rnk
                FROM clients WHERE is_published = 1
            )
            SELECT r.rnk, {ClientColumns}
            FROM ranked r JOIN clients c ON c.id = r.id
            {CoverJoin}
            WHERE c.full_name LIKE @q ESCAPE '\' OR c.headline LIKE @q ESCAPE '\' OR c.location LIKE @q ESCAPE '\'
            ORDER BY CASE
                         WHEN c.full_name LIKE @prefix ESCAPE '\' THEN 0
                         WHEN c.full_name LIKE @word ESCAPE '\' THEN 1
                         WHEN c.full_name LIKE @q ESCAPE '\' THEN 2
                         ELSE 3
                     END, r.rnk
            LIMIT @take
            """)
            .With("@q", Like(query))
            .With("@prefix", Escape(query.Trim()) + "%")
            .With("@word", "% " + Escape(query.Trim()) + "%")
            .With("@take", limit);
        using var reader = cmd.ExecuteReader();
        var list = new List<RankedClient>();
        while (reader.Read()) list.Add(new RankedClient(reader.Int("rnk"), ReadClient(reader)));
        return list;
    }

    // ───────────────────────────── Admin queries ─────────────────────────────

    public List<Client> ListAll()
    {
        using var conn = db.Open();
        using var cmd = conn.Command($"SELECT {ClientColumns} FROM clients c {CoverJoin} ORDER BY c.priority, c.position, c.id");
        using var reader = cmd.ExecuteReader();
        var list = new List<Client>();
        while (reader.Read()) list.Add(ReadClient(reader));
        return list;
    }

    public (int Total, int Published, int Photos, int WorkEntries) Stats()
    {
        using var conn = db.Open();
        using var cmd = conn.Command("""
            SELECT (SELECT COUNT(*) FROM clients),
                   (SELECT COUNT(*) FROM clients WHERE is_published = 1),
                   (SELECT COUNT(*) FROM client_photos) + (SELECT COUNT(*) FROM work_images),
                   (SELECT COUNT(*) FROM work_entries)
            """);
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3));
    }

    public Client? GetById(long id, bool includeDetails = true)
    {
        using var conn = db.Open();
        using var cmd = conn.Command($"SELECT {ClientColumns} FROM clients c {CoverJoin} WHERE c.id = @id").With("@id", id);
        Client? client;
        using (var reader = cmd.ExecuteReader())
        {
            client = reader.Read() ? ReadClient(reader) : null;
        }
        if (client is not null && includeDetails) LoadDetails(conn, client);
        return client;
    }

    public long Create(Client client)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        var now = DateTime.UtcNow;
        var position = Convert.ToInt32(conn.Command("SELECT COALESCE(MAX(position), 0) + 1 FROM clients", tx).ExecuteScalar(),
            CultureInfo.InvariantCulture);
        client.Slug = UniqueSlug(conn, tx, client.FullName, excludeId: null);

        using var cmd = conn.Command("""
            INSERT INTO clients (slug, full_name, headline, age, location, bio, phone, email, tiktok_url, instagram_url,
                                 show_phone, show_email, is_published, private_notes, priority, position, created_at, updated_at)
            VALUES (@slug, @name, @headline, @age, @location, @bio, @phone, @email, @tiktok, @instagram,
                    @showPhone, @showEmail, @published, @notes, @priority, @position, @now, @now);
            SELECT last_insert_rowid();
            """, tx);
        BindClient(cmd, client).With("@position", position).With("@now", now).With("@slug", client.Slug);
        var id = (long)cmd.ExecuteScalar()!;
        // New clients join the end of their priority tier.
        Renumber(conn, tx, "clients", null, null);
        client.Position = PositionOf(conn, tx, id);
        tx.Commit();
        client.Id = id;
        return id;
    }

    public void Update(Client client)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        string currentName;
        int currentPriority;
        using (var current = conn.Command("SELECT full_name, priority FROM clients WHERE id = @id", tx).With("@id", client.Id).ExecuteReader())
        {
            if (!current.Read()) throw new KeyNotFoundException();
            currentName = current.GetString(0);
            currentPriority = current.GetInt32(1);
        }

        var slugSql = "";
        if (!string.Equals(currentName, client.FullName, StringComparison.Ordinal))
        {
            client.Slug = UniqueSlug(conn, tx, client.FullName, client.Id);
            slugSql = "slug = @slug,";
        }

        using var cmd = conn.Command($"""
            UPDATE clients SET {slugSql}
                full_name = @name, headline = @headline, age = @age, location = @location, bio = @bio,
                phone = @phone, email = @email, tiktok_url = @tiktok, instagram_url = @instagram,
                show_phone = @showPhone, show_email = @showEmail, is_published = @published,
                private_notes = @notes, updated_at = @now
            WHERE id = @id
            """, tx);
        BindClient(cmd, client).With("@id", client.Id).With("@now", DateTime.UtcNow);
        if (slugSql.Length > 0) cmd.With("@slug", client.Slug);
        cmd.ExecuteNonQuery();
        if (currentPriority != client.Priority) ChangeTier(conn, tx, client.Id, client.Priority);
        tx.Commit();
    }

    /// <summary>Moves a client to another priority tier, placing them at the end of that tier.</summary>
    public void SetPriority(long id, int priority)
    {
        if (!Priorities.IsValid(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        if (PositionOf(conn, tx, id) == 0) throw new KeyNotFoundException();
        ChangeTier(conn, tx, id, priority);
        tx.Commit();
    }

    private static void ChangeTier(SqliteConnection conn, SqliteTransaction tx, long id, int priority)
    {
        conn.Command("""
            UPDATE clients SET priority = @p, position = (SELECT COALESCE(MAX(position), 0) + 1 FROM clients), updated_at = @now
            WHERE id = @id
            """, tx).With("@p", priority).With("@now", DateTime.UtcNow).With("@id", id).ExecuteNonQuery();
        Renumber(conn, tx, "clients", null, null);
    }

    private static int PositionOf(SqliteConnection conn, SqliteTransaction tx, long id) =>
        Convert.ToInt32(conn.Command("SELECT position FROM clients WHERE id = @id", tx).With("@id", id).ExecuteScalar() ?? 0,
            CultureInfo.InvariantCulture);

    public void SetPublished(long id, bool published)
    {
        using var conn = db.Open();
        conn.Command("UPDATE clients SET is_published = @p, updated_at = @now WHERE id = @id")
            .With("@p", published).With("@now", DateTime.UtcNow).With("@id", id).ExecuteNonQuery();
    }

    /// <summary>Deletes a client with all photos and work history; returns the media files to remove.</summary>
    public List<string> Delete(long id)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        var files = new List<string>();
        using (var cmd = conn.Command("""
            SELECT file, thumb FROM client_photos WHERE client_id = @id
            UNION ALL
            SELECT i.file, i.thumb FROM work_images i JOIN work_entries e ON e.id = i.entry_id WHERE e.client_id = @id
            """, tx).With("@id", id))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) { files.Add(r.GetString(0)); files.Add(r.GetString(1)); }
        }
        conn.Command("DELETE FROM clients WHERE id = @id", tx).With("@id", id).ExecuteNonQuery();
        Renumber(conn, tx, "clients", null, null);
        tx.Commit();
        return files;
    }

    // ───────────────────────────── Ranking ─────────────────────────────

    /// <summary>
    /// Saves the full homepage order exactly as given (first id = position 1), together with each
    /// client's priority tier (drag-and-drop can move a client across tier boundaries).
    /// </summary>
    public void ReorderClients(IReadOnlyList<long> orderedIds, IReadOnlyList<int>? priorities)
    {
        if (priorities is not null && (priorities.Count != orderedIds.Count || !priorities.All(Priorities.IsValid)))
            throw new InvalidOperationException("The submitted priorities are invalid. Refresh the page and try again.");
        if (priorities is not null)
        {
            // Tiers must be non-decreasing down the list, otherwise the order couldn't be displayed as given.
            for (var i = 1; i < priorities.Count; i++)
                if (priorities[i] < priorities[i - 1])
                    throw new InvalidOperationException("The submitted order doesn't match the priority groups. Refresh the page and try again.");
        }

        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        if (priorities is not null)
        {
            using var cmd = conn.Command("UPDATE clients SET priority = @p WHERE id = @id", tx);
            var p = cmd.Parameters.Add("@p", SqliteType.Integer);
            var i = cmd.Parameters.Add("@id", SqliteType.Integer);
            for (var n = 0; n < orderedIds.Count; n++)
            {
                p.Value = priorities[n];
                i.Value = orderedIds[n];
                cmd.ExecuteNonQuery();
            }
        }
        ApplyOrder(conn, tx, "clients", null, null, orderedIds);
        tx.Commit();
    }

    /// <summary>
    /// Moves one client to a 1-based overall position, shifting the others. The client joins the
    /// priority tier of the place they land in (the client they displace, or the last client).
    /// </summary>
    public void MoveClient(long id, int newPosition)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        var ids = OrderedIds(conn, tx, "clients", null, null);
        if (!ids.Remove(id)) throw new KeyNotFoundException();
        var index = Math.Clamp(newPosition, 1, ids.Count + 1) - 1;
        if (ids.Count > 0)
        {
            var anchor = index < ids.Count ? ids[index] : ids[^1];
            var tier = Convert.ToInt32(conn.Command("SELECT priority FROM clients WHERE id = @id", tx).With("@id", anchor).ExecuteScalar(),
                CultureInfo.InvariantCulture);
            conn.Command("UPDATE clients SET priority = @p WHERE id = @id", tx).With("@p", tier).With("@id", id).ExecuteNonQuery();
        }
        ids.Insert(index, id);
        WritePositions(conn, tx, "clients", ids);
        tx.Commit();
    }

    // ───────────────────────────── Photos ─────────────────────────────

    public void AddPhotos(long clientId, IEnumerable<NewImage> images) =>
        AddImages("client_photos", "client_id", clientId, images);

    public List<string> DeletePhoto(long clientId, long photoId) =>
        DeleteImage("client_photos", "client_id", clientId, photoId);

    public void ReorderPhotos(long clientId, IReadOnlyList<long> orderedIds)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        ApplyOrder(conn, tx, "client_photos", "client_id", clientId, orderedIds);
        tx.Commit();
    }

    public void MakeCover(long clientId, long photoId)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        MoveTo(conn, tx, "client_photos", "client_id", clientId, photoId, 1);
        tx.Commit();
    }

    // ───────────────────────────── Work history ─────────────────────────────

    public WorkEntry? GetWorkEntry(long clientId, long entryId)
    {
        using var conn = db.Open();
        return LoadWork(conn, clientId, entryId).FirstOrDefault();
    }

    public long CreateWorkEntry(WorkEntry entry)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        var position = Convert.ToInt32(conn.Command(
            "SELECT COALESCE(MAX(position), 0) + 1 FROM work_entries WHERE client_id = @c", tx)
            .With("@c", entry.ClientId).ExecuteScalar(), CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow;
        using var cmd = conn.Command("""
            INSERT INTO work_entries (client_id, title, service, description, work_date, links_json, position, created_at, updated_at)
            VALUES (@c, @title, @service, @description, @date, @links, @position, @now, @now);
            SELECT last_insert_rowid();
            """, tx);
        BindWork(cmd, entry).With("@c", entry.ClientId).With("@position", position).With("@now", now);
        var id = (long)cmd.ExecuteScalar()!;
        Touch(conn, tx, entry.ClientId);
        tx.Commit();
        entry.Id = id;
        return id;
    }

    public bool UpdateWorkEntry(WorkEntry entry)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.Command("""
            UPDATE work_entries SET title = @title, service = @service, description = @description,
                   work_date = @date, links_json = @links, updated_at = @now
            WHERE id = @id AND client_id = @c
            """, tx);
        BindWork(cmd, entry).With("@id", entry.Id).With("@c", entry.ClientId).With("@now", DateTime.UtcNow);
        var changed = cmd.ExecuteNonQuery() > 0;
        if (changed) Touch(conn, tx, entry.ClientId);
        tx.Commit();
        return changed;
    }

    public List<string> DeleteWorkEntry(long clientId, long entryId)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        var files = new List<string>();
        using (var cmd = conn.Command("""
            SELECT i.file, i.thumb FROM work_images i JOIN work_entries e ON e.id = i.entry_id
            WHERE e.id = @e AND e.client_id = @c
            """, tx).With("@e", entryId).With("@c", clientId))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) { files.Add(r.GetString(0)); files.Add(r.GetString(1)); }
        }
        var deleted = conn.Command("DELETE FROM work_entries WHERE id = @e AND client_id = @c", tx)
            .With("@e", entryId).With("@c", clientId).ExecuteNonQuery();
        if (deleted > 0)
        {
            Renumber(conn, tx, "work_entries", "client_id", clientId);
            Touch(conn, tx, clientId);
        }
        tx.Commit();
        return files;
    }

    public void ReorderWork(long clientId, IReadOnlyList<long> orderedIds)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        ApplyOrder(conn, tx, "work_entries", "client_id", clientId, orderedIds);
        Touch(conn, tx, clientId);
        tx.Commit();
    }

    public void MoveWork(long clientId, long entryId, int newPosition)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        MoveTo(conn, tx, "work_entries", "client_id", clientId, entryId, newPosition);
        Touch(conn, tx, clientId);
        tx.Commit();
    }

    public bool WorkEntryBelongsTo(long clientId, long entryId)
    {
        using var conn = db.Open();
        return conn.Command("SELECT 1 FROM work_entries WHERE id = @e AND client_id = @c")
            .With("@e", entryId).With("@c", clientId).ExecuteScalar() is not null;
    }

    public void AddWorkImages(long entryId, IEnumerable<NewImage> images) =>
        AddImages("work_images", "entry_id", entryId, images);

    public List<string> DeleteWorkImage(long entryId, long imageId) =>
        DeleteImage("work_images", "entry_id", entryId, imageId);

    public void ReorderWorkImages(long entryId, IReadOnlyList<long> orderedIds)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        ApplyOrder(conn, tx, "work_images", "entry_id", entryId, orderedIds);
        tx.Commit();
    }

    // ───────────────────────────── Internals ─────────────────────────────

    private void LoadDetails(SqliteConnection conn, Client client)
    {
        client.Photos = LoadImages(conn, "client_photos", "client_id", client.Id);
        client.Work = LoadWork(conn, client.Id, null);
    }

    private static List<Photo> LoadImages(SqliteConnection conn, string table, string ownerColumn, long ownerId)
    {
        using var cmd = conn.Command($"""
            SELECT id, {ownerColumn} AS owner_id, file, thumb, width, height, position
            FROM {table} WHERE {ownerColumn} = @o ORDER BY position, id
            """).With("@o", ownerId);
        using var r = cmd.ExecuteReader();
        var list = new List<Photo>();
        while (r.Read()) list.Add(ReadPhoto(r));
        return list;
    }

    private static List<WorkEntry> LoadWork(SqliteConnection conn, long clientId, long? entryId)
    {
        var entries = new List<WorkEntry>();
        using (var cmd = conn.Command($"""
            SELECT id, client_id, title, service, description, work_date, links_json, position
            FROM work_entries WHERE client_id = @c {(entryId is null ? "" : "AND id = @e")}
            ORDER BY position, id
            """).With("@c", clientId))
        {
            if (entryId is not null) cmd.With("@e", entryId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                entries.Add(new WorkEntry
                {
                    Id = r.Long("id"),
                    ClientId = r.Long("client_id"),
                    Title = r.Str("title")!,
                    Service = r.Str("service"),
                    Description = r.Str("description"),
                    WorkDate = r.Str("work_date"),
                    Links = JsonSerializer.Deserialize<List<WorkLink>>(r.Str("links_json") ?? "[]") ?? [],
                    Position = r.Int("position"),
                });
            }
        }
        if (entries.Count == 0) return entries;

        var byId = entries.ToDictionary(e => e.Id);
        using var img = conn.Command("""
            SELECT i.id, i.entry_id AS owner_id, i.file, i.thumb, i.width, i.height, i.position
            FROM work_images i JOIN work_entries e ON e.id = i.entry_id
            WHERE e.client_id = @c ORDER BY i.position, i.id
            """).With("@c", clientId);
        using var ir = img.ExecuteReader();
        while (ir.Read())
        {
            var photo = ReadPhoto(ir);
            if (byId.TryGetValue(photo.OwnerId, out var entry)) entry.Images.Add(photo);
        }
        return entries;
    }

    private void AddImages(string table, string ownerColumn, long ownerId, IEnumerable<NewImage> images)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        var position = Convert.ToInt32(conn.Command(
            $"SELECT COALESCE(MAX(position), 0) FROM {table} WHERE {ownerColumn} = @o", tx)
            .With("@o", ownerId).ExecuteScalar(), CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow;
        foreach (var image in images)
        {
            conn.Command($"""
                INSERT INTO {table} ({ownerColumn}, file, thumb, width, height, position, created_at)
                VALUES (@o, @file, @thumb, @w, @h, @pos, @now)
                """, tx)
                .With("@o", ownerId).With("@file", image.File).With("@thumb", image.Thumb)
                .With("@w", image.Width).With("@h", image.Height).With("@pos", ++position).With("@now", now)
                .ExecuteNonQuery();
        }
        tx.Commit();
    }

    private List<string> DeleteImage(string table, string ownerColumn, long ownerId, long imageId)
    {
        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        var files = new List<string>();
        using (var cmd = conn.Command($"SELECT file, thumb FROM {table} WHERE id = @id AND {ownerColumn} = @o", tx)
            .With("@id", imageId).With("@o", ownerId))
        using (var r = cmd.ExecuteReader())
        {
            if (r.Read()) { files.Add(r.GetString(0)); files.Add(r.GetString(1)); }
        }
        if (files.Count > 0)
        {
            conn.Command($"DELETE FROM {table} WHERE id = @id", tx).With("@id", imageId).ExecuteNonQuery();
            Renumber(conn, tx, table, ownerColumn, ownerId);
        }
        tx.Commit();
        return files;
    }

    private static List<long> OrderedIds(SqliteConnection conn, SqliteTransaction tx, string table, string? scopeColumn, long? scopeId)
    {
        // Clients are ordered by priority tier first; everything else by position alone.
        var order = table == "clients" ? "priority, position, id" : "position, id";
        using var cmd = conn.Command(
            $"SELECT id FROM {table} {(scopeColumn is null ? "" : $"WHERE {scopeColumn} = @s")} ORDER BY {order}", tx);
        if (scopeColumn is not null) cmd.With("@s", scopeId);
        using var r = cmd.ExecuteReader();
        var ids = new List<long>();
        while (r.Read()) ids.Add(r.GetInt64(0));
        return ids;
    }

    private static void ApplyOrder(SqliteConnection conn, SqliteTransaction tx, string table, string? scopeColumn, long? scopeId,
        IReadOnlyList<long> orderedIds)
    {
        var existing = OrderedIds(conn, tx, table, scopeColumn, scopeId);
        if (orderedIds.Count != existing.Count || orderedIds.Distinct().Count() != orderedIds.Count ||
            !existing.ToHashSet().SetEquals(orderedIds))
        {
            throw new InvalidOperationException("The submitted order is out of date. Refresh the page and try again.");
        }
        WritePositions(conn, tx, table, orderedIds);
    }

    private static void MoveTo(SqliteConnection conn, SqliteTransaction tx, string table, string? scopeColumn, long? scopeId,
        long id, int newPosition)
    {
        var ids = OrderedIds(conn, tx, table, scopeColumn, scopeId);
        if (!ids.Remove(id)) throw new KeyNotFoundException();
        ids.Insert(Math.Clamp(newPosition, 1, ids.Count + 1) - 1, id);
        WritePositions(conn, tx, table, ids);
    }

    private static void Renumber(SqliteConnection conn, SqliteTransaction tx, string table, string? scopeColumn, long? scopeId) =>
        WritePositions(conn, tx, table, OrderedIds(conn, tx, table, scopeColumn, scopeId));

    private static void WritePositions(SqliteConnection conn, SqliteTransaction tx, string table, IReadOnlyList<long> ids)
    {
        using var cmd = conn.Command($"UPDATE {table} SET position = @p WHERE id = @id", tx);
        var p = cmd.Parameters.Add("@p", SqliteType.Integer);
        var i = cmd.Parameters.Add("@id", SqliteType.Integer);
        for (var n = 0; n < ids.Count; n++)
        {
            p.Value = n + 1;
            i.Value = ids[n];
            cmd.ExecuteNonQuery();
        }
    }

    private static void Touch(SqliteConnection conn, SqliteTransaction tx, long clientId) =>
        conn.Command("UPDATE clients SET updated_at = @now WHERE id = @id", tx)
            .With("@now", DateTime.UtcNow).With("@id", clientId).ExecuteNonQuery();

    private static string UniqueSlug(SqliteConnection conn, SqliteTransaction tx, string name, long? excludeId)
    {
        var root = Slugify(name);
        var candidate = root;
        for (var n = 2; ; n++)
        {
            using var cmd = conn.Command("SELECT 1 FROM clients WHERE slug = @s AND id <> @id", tx)
                .With("@s", candidate).With("@id", excludeId ?? -1);
            if (cmd.ExecuteScalar() is null) return candidate;
            candidate = $"{root}-{n}";
        }
    }

    /// <summary>Lower-case, hyphenated slug that keeps letters from any script (Latin, Arabic, …).</summary>
    public static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        var dash = false;
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) continue;
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
            if (sb.Length >= 80) break;
        }
        var slug = sb.ToString().Trim('-').Normalize(NormalizationForm.FormC);
        return slug.Length == 0 ? "client" : slug;
    }

    private static SqliteCommand BindClient(SqliteCommand cmd, Client c) => cmd
        .With("@name", c.FullName).With("@headline", c.Headline).With("@age", c.Age).With("@location", c.Location)
        .With("@bio", c.Bio).With("@phone", c.Phone).With("@email", c.Email).With("@tiktok", c.TikTokUrl)
        .With("@instagram", c.InstagramUrl).With("@showPhone", c.ShowPhone).With("@showEmail", c.ShowEmail)
        .With("@published", c.IsPublished).With("@notes", c.PrivateNotes).With("@priority", c.Priority);

    private static SqliteCommand BindWork(SqliteCommand cmd, WorkEntry e) => cmd
        .With("@title", e.Title).With("@service", e.Service).With("@description", e.Description)
        .With("@date", e.WorkDate).With("@links", JsonSerializer.Serialize(e.Links));

    private static Client ReadClient(SqliteDataReader r)
    {
        var client = new Client
        {
            Id = r.Long("id"),
            Slug = r.Str("slug")!,
            FullName = r.Str("full_name")!,
            Headline = r.Str("headline"),
            Age = r.NullableInt("age"),
            Location = r.Str("location"),
            Bio = r.Str("bio"),
            Phone = r.Str("phone"),
            Email = r.Str("email"),
            TikTokUrl = r.Str("tiktok_url"),
            InstagramUrl = r.Str("instagram_url"),
            ShowPhone = r.Bool("show_phone"),
            ShowEmail = r.Bool("show_email"),
            IsPublished = r.Bool("is_published"),
            PrivateNotes = r.Str("private_notes"),
            Priority = r.Int("priority"),
            Position = r.Int("position"),
            CreatedAt = r.Date("created_at"),
            UpdatedAt = r.Date("updated_at"),
            PhotoCount = r.Int("photo_count"),
            WorkCount = r.Int("work_count"),
        };
        if (r.NullableInt("p_id") is not null)
        {
            client.Cover = new Photo
            {
                Id = r.Long("p_id"),
                OwnerId = client.Id,
                File = r.Str("p_file")!,
                Thumb = r.Str("p_thumb")!,
                Width = r.Int("p_width"),
                Height = r.Int("p_height"),
            };
        }
        return client;
    }

    private static Photo ReadPhoto(SqliteDataReader r) => new()
    {
        Id = r.Long("id"),
        OwnerId = r.Long("owner_id"),
        File = r.Str("file")!,
        Thumb = r.Str("thumb")!,
        Width = r.Int("width"),
        Height = r.Int("height"),
        Position = r.Int("position"),
    };

    private static string Like(string search) => "%" + Escape(search.Trim()) + "%";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
