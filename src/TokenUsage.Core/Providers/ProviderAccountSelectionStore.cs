using System.Text.Json;
using TokenUsage.Core.Storage;

namespace TokenUsage.Core.Providers;

public sealed record ProviderAccountSelection(string AccountKey, int Number, string? Alias);

/// <summary>Only opaque identities and user-facing labels belong in this document.</summary>
public sealed class ProviderAccountSelectionStore
{
    private const int MaximumBytes = 64 * 1024;
    private readonly ProviderId _provider;
    private readonly VersionedDocumentFile _document;

    public ProviderAccountSelectionStore(string localState, ProviderId provider, TimeProvider? clock = null)
    {
        _provider = provider;
        Root = Path.Combine(Path.GetFullPath(localState), "accounts", provider.Value);
        _document = new VersionedDocumentFile(Path.Combine(Root, "selection.v1.json"),
            "TokenUsage.AccountSelection", clock ?? TimeProvider.System);
    }

    public string Root { get; }

    // Once this namespace exists, missing settings must never reactivate the legacy writer.
    public bool WasActivated
    {
        get
        {
            try { _ = File.GetAttributes(Root); return true; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or System.Security.SecurityException) { return true; }
        }
    }

    public string AccountDirectory(string key) => Path.Combine(Root, new ProviderInstanceKey(_provider, key).AccountKey!);

    public Task<IReadOnlyList<ProviderAccountSelection>> LoadAsync(CancellationToken token = default) =>
        _document.RunLockedAsync(LoadCore, token);

    public Task SaveAsync(IReadOnlyList<ProviderAccountSelection> selection, CancellationToken token = default) =>
        _document.RunLockedAsync(() =>
        {
            _ = LoadCore(); // An unreadable or future document cannot be overwritten.
            Validate(selection);
            _document.WriteAtomically(JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                providerId = _provider.Value,
                accounts = selection.Select(item => new { accountKey = item.AccountKey, number = item.Number, alias = item.Alias }),
            }), MaximumBytes);
            return true;
        }, token);

    private IReadOnlyList<ProviderAccountSelection> LoadCore()
    {
        if (!_document.ExistsStrict) return [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(_document.ReadBoundedBytes(MaximumBytes),
                new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1
                || root.GetProperty("providerId").GetString() != _provider.Value)
                throw new IOException("Account selection has an unsupported identity or version.");
            ProviderAccountSelection[] selection = root.GetProperty("accounts").EnumerateArray()
                .Select(item => new ProviderAccountSelection(item.GetProperty("accountKey").GetString()!,
                    item.GetProperty("number").GetInt32(), item.GetProperty("alias").GetString())).ToArray();
            Validate(selection);
            return selection;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or VersionedDocumentFormatException)
        {
            throw new IOException("Account selection is unreadable; the original file was preserved.");
        }
    }

    private void Validate(IReadOnlyList<ProviderAccountSelection> selection)
    {
        if (selection.Count > 64 || selection.Select(item => item.AccountKey).Distinct(StringComparer.Ordinal).Count() != selection.Count)
            throw new ArgumentException("Account selection contains too many or duplicate accounts.");
        foreach (ProviderAccountSelection item in selection)
        {
            ArgumentException.ThrowIfNullOrEmpty(item.AccountKey);
            _ = new ProviderInstanceKey(_provider, item.AccountKey);
            if (item.Number < 1 || item.Alias is { Length: > 80 }
                || item.Alias?.Any(char.IsControl) is true)
                throw new ArgumentException("Account labels are invalid.");
        }
    }
}
