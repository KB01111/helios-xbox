using Microsoft.Data.Sqlite;

namespace Helios.Library;

public sealed class LibraryStore
{
    private readonly string _connectionString;

    public LibraryStore(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS games (
                id TEXT PRIMARY KEY, title TEXT NOT NULL, source INTEGER NOT NULL,
                target TEXT NOT NULL, artwork TEXT, available INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS preferences (
                id TEXT PRIMARY KEY, favorite INTEGER NOT NULL DEFAULT 0, last_launch INTEGER, pinned INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Opens the library and proves it can be read. A database that SQLite reports as corrupt or not a database is
    /// moved aside (with its WAL files) and replaced by an empty library, so the Xbox mode home never stays broken.
    /// </summary>
    /// <returns>The store, and the path of the moved-aside file when recovery happened.</returns>
    public static (LibraryStore Store, string? Quarantined) OpenOrRecover(string path)
    {
        try
        {
            var store = new LibraryStore(path);
            store.ReadGames();
            store.GetSetting("theme");
            return (store, null);
        }
        catch (SqliteException error) when (error.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            SqliteConnection.ClearAllPools();
            string quarantined = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Move(path, quarantined, overwrite: true);
            foreach (string suffix in (string[])["-wal", "-shm"])
            {
                if (File.Exists(path + suffix))
                    File.Move(path + suffix, quarantined + suffix, overwrite: true);
            }
            return (new LibraryStore(path), quarantined);
        }
    }

    /// <summary>
    /// Reads settings without creating or migrating the database, so the window can pick its mode and accent before
    /// the first frame. Missing, locked or damaged files return no values.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PeekSettings(string path, params string[] keys)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (keys.Length == 0 || !File.Exists(path))
            return values;
        try
        {
            string connection = new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1,
            }.ToString();
            using var db = new SqliteConnection(connection);
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT value FROM settings WHERE key = $key";
            var parameter = command.Parameters.Add("$key", SqliteType.Text);
            foreach (string key in keys)
            {
                parameter.Value = key;
                if (command.ExecuteScalar() is string value)
                    values[key] = value;
            }
        }
        catch (SqliteException)
        {
            // OpenOrRecover deals with damaged files once the library loads.
        }
        return values;
    }

    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    public IReadOnlyList<GameEntry> ReadGames()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT g.id, g.title, g.source, g.target, g.artwork, g.available, COALESCE(p.favorite, 0), p.last_launch, COALESCE(p.pinned, 0)
            FROM games g LEFT JOIN preferences p ON p.id = g.id
            ORDER BY g.title COLLATE NOCASE, g.id
            """;
        using var reader = command.ExecuteReader();
        var entries = new List<GameEntry>();
        while (reader.Read())
        {
            entries.Add(new GameEntry(reader.GetString(0), reader.GetString(1), (GameSource)reader.GetInt32(2),
                reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetBoolean(5),
                reader.GetBoolean(6), reader.IsDBNull(7) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
                reader.GetBoolean(8)));
        }

        return entries;
    }

    public void ApplyScan(GameSource source, IEnumerable<GameEntry> games, bool complete)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        if (complete)
        {
            using var reset = connection.CreateCommand();
            reset.Transaction = transaction;
            reset.CommandText = "UPDATE games SET available = 0 WHERE source = $source";
            reset.Parameters.AddWithValue("$source", (int)source);
            reset.ExecuteNonQuery();
        }

        using var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO games (id, title, source, target, artwork, available)
            VALUES ($id, $title, $source, $target, $artwork, $available)
            ON CONFLICT(id) DO UPDATE SET title = excluded.title, target = excluded.target,
                artwork = COALESCE(excluded.artwork, games.artwork), available = excluded.available
            WHERE games.source = excluded.source
            """;
        upsert.Parameters.Add("$id", SqliteType.Text);
        upsert.Parameters.Add("$title", SqliteType.Text);
        upsert.Parameters.AddWithValue("$source", (int)source);
        upsert.Parameters.Add("$target", SqliteType.Text);
        upsert.Parameters.Add("$artwork", SqliteType.Text);
        upsert.Parameters.Add("$available", SqliteType.Integer);

        foreach (var game in games)
        {
            if (game.Source != source || string.IsNullOrWhiteSpace(game.Title) || !GameEntry.IsValidTarget(source, game.Target))
                throw new ArgumentException("The game does not have a valid target for its source.", nameof(games));
            upsert.Parameters["$id"].Value = game.Id;
            upsert.Parameters["$title"].Value = game.Title;
            upsert.Parameters["$target"].Value = game.Target;
            upsert.Parameters["$artwork"].Value = (object?)game.Artwork ?? DBNull.Value;
            upsert.Parameters["$available"].Value = game.IsAvailable;
            upsert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void SetFavorite(string id, bool favorite) => Execute("""
        INSERT INTO preferences (id, favorite) VALUES ($key, $value)
        ON CONFLICT(id) DO UPDATE SET favorite = excluded.favorite
        """, id, favorite);

    public void RecordLaunch(string id, DateTimeOffset date) => Execute("""
        INSERT INTO preferences (id, last_launch) VALUES ($key, $value)
        ON CONFLICT(id) DO UPDATE SET last_launch = excluded.last_launch
        """, id, date.ToUnixTimeMilliseconds());

    public void SetPinned(string id, bool pinned) => Execute("""
        INSERT INTO preferences (id, pinned) VALUES ($key, $value)
        ON CONFLICT(id) DO UPDATE SET pinned = excluded.pinned
        """, id, pinned);

    public void RemoveGame(string id) => Execute("DELETE FROM games WHERE id = $key", id);

    public string? GetSetting(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value) => Execute("""
        INSERT INTO settings (key, value) VALUES ($key, $value)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value
        """, key, value);

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Execute(string sql, string key, object? value = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$key", key);
        if (value is not null)
            command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
