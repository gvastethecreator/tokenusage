using System.Text.Json;
using TokenUsage.Core.Storage;
using TokenUsage.Core.Updates;

namespace TokenUsage.App.Services.Updates;

public sealed record PendingUpdate(UpdateRelease Release, string InstallDirectory, string? PlanPath = null);

/// <summary>Keeps the prepared update recoverable across application lifetimes.</summary>
public sealed class PendingUpdateStore
{
    private const int MaximumBytes = 16 * 1024;
    private readonly VersionedDocumentFile _document;
    private readonly string _installDirectory;
    private readonly string _portableDirectory;

    public PendingUpdateStore(string updateDirectory, string installDirectory)
    {
        _document = new VersionedDocumentFile(Path.Combine(updateDirectory, "pending.v1.json"),
            "TokenUsage.PendingUpdateStore", TimeProvider.System);
        _installDirectory = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar);
        _portableDirectory = Path.GetFullPath(Path.Combine(updateDirectory, "portable")) + Path.DirectorySeparatorChar;
    }

    public Task<PendingUpdate?> LoadAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() =>
        {
            if (!_document.Exists) return null;
            try
            {
                Document? document = JsonSerializer.Deserialize<Document>(
                    _document.ReadBoundedBytes(MaximumBytes));
                if (document?.SchemaVersion != 1 || document.Pending is null)
                    throw new InvalidDataException("Invalid pending update document.");
                Validate(document.Pending);
                return document.Pending;
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException
                or VersionedDocumentFormatException or ArgumentException)
            {
                _document.QuarantineCorrupt();
                return null;
            }
        }, cancellationToken);

    public Task SaveAsync(PendingUpdate pending, CancellationToken cancellationToken = default)
    {
        Validate(pending);
        return _document.RunLockedAsync(() =>
        {
            _document.WriteAtomically(JsonSerializer.SerializeToUtf8Bytes(new Document(1, pending)), MaximumBytes);
            return true;
        }, cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        _document.RunLockedAsync(() =>
        {
            if (_document.Exists) File.Delete(_document.DocumentPath);
            return true;
        }, cancellationToken);

    private void Validate(PendingUpdate pending)
    {
        if (pending.Release is null || pending.Release.Version is null
            || !Enum.IsDefined(pending.Release.Kind)
            || !string.Equals(Path.GetFullPath(pending.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar),
                _installDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The pending update belongs to another installation.");
        if (pending.Release.Kind == UpdatePackageKind.Portable
            && (pending.PlanPath is null
                || !Path.GetFullPath(pending.PlanPath).StartsWith(_portableDirectory, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(pending.PlanPath) != "plan.json"))
            throw new InvalidDataException("The pending portable plan is outside this app's update directory.");
    }

    private sealed record Document(int SchemaVersion, PendingUpdate Pending);
}
