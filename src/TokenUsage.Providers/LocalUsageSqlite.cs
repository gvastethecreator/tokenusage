using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TokenUsage.Providers;

/// <summary>Shared mechanics for read-only local sources; queries and transactions stay with each source.</summary>
internal static class LocalUsageSqlite
{
    public static void ExecuteControl(
        SqliteConnection connection,
        string text,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = text;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        command.ExecuteNonQuery();
    }

    public static HashSet<string> GetColumns(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        using SqliteDataReader reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    public static bool TryGetInt64(SqliteDataReader reader, int ordinal, out long value)
    {
        value = 0;
        if (reader.IsDBNull(ordinal))
        {
            return false;
        }

        object raw = reader.GetValue(ordinal);
        return raw switch
        {
            long number when number >= 0 => Assign(number, out value),
            int number when number >= 0 => Assign(number, out value),
            string text when long.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long number) && number >= 0 => Assign(number, out value),
            _ => false,
        };
    }

    public static long GetNonNegativeOrZero(SqliteDataReader reader, int ordinal) =>
        TryGetInt64(reader, ordinal, out long value) ? value : 0;

    private static bool Assign(long source, out long destination)
    {
        destination = source;
        return true;
    }
}
