using Microsoft.Data.Sqlite;
using TokenUsage.Providers.LocalScan;

namespace TokenUsage.Providers.Codex;

public sealed partial class CodexUsageEventSource
{
    public bool IsRootAvailable => SessionRoots().Any(Directory.Exists);

    private SessionFile[] FindSessionFiles(
        string[] roots,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        SessionFile[]? indexed = ReadStateIndex(roots, state, cancellationToken);
        var bySession = new Dictionary<string, SessionFile>(StringComparer.OrdinalIgnoreCase);
        if (indexed is { Length: > 0 })
        {
            foreach (SessionFile file in indexed)
            {
                AddSessionFile(bySession, file);
            }
        }

        foreach (string root in roots)
        {
            foreach (string path in EnumerateJsonlFiles(root, state, cancellationToken))
            {
                AddSessionFile(bySession, new SessionFile(
                    path,
                    SessionIdentity(path),
                    Model: null));
            }
        }

        return bySession.Values
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private SessionFile[]? ReadStateIndex(
        string[] roots,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        string databasePath = Path.Combine(_codexHome, "state_5.sqlite");
        if (!File.Exists(databasePath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(databasePath);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                state.MarkPartial();
                return null;
            }

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            ExecuteControl(connection, "PRAGMA busy_timeout=250", cancellationToken);
            ExecuteControl(connection, "PRAGMA query_only=ON", cancellationToken);

            HashSet<string> columns = GetColumns(connection, "threads", cancellationToken);
            if (!columns.Contains("rollout_path") || !columns.Contains("model"))
            {
                state.UnsupportedSchema = true;
                state.MarkPartial();
                return null;
            }

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT rollout_path, model FROM threads "
                + "WHERE rollout_path IS NOT NULL AND rollout_path <> '' "
                + "ORDER BY rollout_path LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", checked(_budget.MaximumFiles + 1L));
            using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
            using SqliteDataReader reader = command.ExecuteReader();
            var bySession = new Dictionary<string, SessionFile>(StringComparer.OrdinalIgnoreCase);
            int rowsRead = 0;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++rowsRead > _budget.MaximumFiles)
                {
                    state.MarkPartial();
                    break;
                }

                string rawPath = reader.GetString(0);
                string? path = ResolveIndexedPath(rawPath, roots);
                if (path is null)
                {
                    state.MarkPartial();
                    continue;
                }

                string? model = reader.IsDBNull(1) ? null : reader.GetString(1);
                AddSessionFile(bySession, new SessionFile(
                    path,
                    SessionIdentity(path),
                    NormalizeModel(model)));
            }

            return bySession.Values
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException
                                           or IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            state.MarkPartial();
            return null;
        }
    }

    private static void ExecuteControl(
        SqliteConnection connection,
        string text,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = text;
        using CancellationTokenRegistration registration = cancellationToken.Register(command.Cancel);
        command.ExecuteNonQuery();
    }

    private static HashSet<string> GetColumns(
        SqliteConnection connection,
        string table,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
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

    private static string? ResolveIndexedPath(string rawPath, string[] roots)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string path;
        try
        {
            path = Path.GetFullPath(RemoveExtendedPathPrefix(rawPath));
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return null;
        }

        return string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase)
               && roots.Any(root => IsWithinRoot(path, root))
            ? path
            : null;
    }

    private static string RemoveExtendedPathPrefix(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[devicePrefix.Length..]
            : path;
    }

    private static bool IsWithinRoot(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative)
               && !string.Equals(relative, "..", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void AddSessionFile(
        Dictionary<string, SessionFile> files,
        SessionFile candidate)
    {
        if (!files.TryGetValue(candidate.SessionIdentity, out SessionFile? current))
        {
            files.Add(candidate.SessionIdentity, candidate);
            return;
        }

        long currentLength = TryGetLength(current.Path);
        long candidateLength = TryGetLength(candidate.Path);
        if (candidateLength > currentLength
            || (candidateLength == currentLength
                && current.Model is null
                && candidate.Model is not null))
        {
            files[candidate.SessionIdentity] = candidate;
        }
    }

    private static long TryGetLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return -1;
        }
    }

    private DateOnly RecentFrom()
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_groupingTimeZoneId);
        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), timeZone).DateTime);
        return today.AddDays(-(RecentLocalWindowDays - 1));
    }

    private static DateTime TryGetLastWriteTimeUtc(SessionFile file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file.Path);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or NotSupportedException
                                           or System.Security.SecurityException)
        {
            return DateTime.MinValue;
        }
    }

    private static IEnumerable<string> EnumerateJsonlFiles(
        string root,
        LocalScanState state,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out string? directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] files;
            string[] children;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                children = Directory.EnumerateDirectories(directory)
                    .OrderDescending(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.Security.SecurityException)
            {
                state.MarkPartial();
                continue;
            }

            foreach (string file in files)
            {
                bool include = false;
                try
                {
                    include = (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0;
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or System.Security.SecurityException)
                {
                    state.MarkPartial();
                }

                if (include)
                {
                    yield return file;
                }
                else
                {
                    state.MarkPartial();
                }
            }

            foreach (string child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                    else
                    {
                        state.MarkPartial();
                    }
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or System.Security.SecurityException)
                {
                    state.MarkPartial();
                }
            }
        }
    }

    private string[] SessionRoots() =>
    [
        Path.Combine(_codexHome, "sessions"),
        Path.Combine(_codexHome, "archived_sessions"),
    ];

    private static string ResolveHome(string? configured, string userHome)
    {
        string raw = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(userHome, ".codex")
            : configured.Trim();
        if (raw == "~")
        {
            raw = userHome;
        }
        else if (raw.StartsWith($"~{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                 || raw.StartsWith($"~{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            raw = Path.Combine(userHome, raw[2..]);
        }

        try
        {
            return Path.GetFullPath(raw);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return Path.GetFullPath(Path.Combine(userHome, ".codex"));
        }
    }

    private static string SessionIdentity(string path) =>
        Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
}
