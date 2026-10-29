using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ClientShowcase.Data;

internal static class SqlExtensions
{
    public static SqliteCommand Command(this SqliteConnection connection, string sql, SqliteTransaction? tx = null)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }

    public static SqliteCommand With(this SqliteCommand cmd, string name, object? value)
    {
        cmd.Parameters.AddWithValue(name, value switch
        {
            null => DBNull.Value,
            bool b => b ? 1 : 0,
            DateTime d => d.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            string s when s.Length == 0 => DBNull.Value,
            _ => value,
        });
        return cmd;
    }

    public static string? Str(this SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    public static long Long(this SqliteDataReader r, string column) => r.GetInt64(r.GetOrdinal(column));

    public static int Int(this SqliteDataReader r, string column) => r.GetInt32(r.GetOrdinal(column));

    public static int? NullableInt(this SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetInt32(i);
    }

    public static bool Bool(this SqliteDataReader r, string column) => r.GetInt64(r.GetOrdinal(column)) != 0;

    public static DateTime Date(this SqliteDataReader r, string column) =>
        DateTime.Parse(r.GetString(r.GetOrdinal(column)), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTime? NullableDate(this SqliteDataReader r, string column)
    {
        var s = r.Str(column);
        return s is null ? null : DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
