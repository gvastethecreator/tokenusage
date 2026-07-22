using WOpenUsage.Core.Providers;

namespace WOpenUsage.Core.Cache;

public abstract class SnapshotCacheReadResult
{
    private SnapshotCacheReadResult()
    {
    }

    public sealed class Empty : SnapshotCacheReadResult;

    public sealed class Loaded : SnapshotCacheReadResult
    {
        public Loaded(IEnumerable<ProviderSnapshot> snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshots);
            ProviderSnapshot[] snapshotArray = snapshots.ToArray();
            if (snapshotArray.Any(snapshot => snapshot is null))
            {
                throw new ArgumentException("Snapshots cannot contain null values.", nameof(snapshots));
            }

            Snapshots = Array.AsReadOnly(snapshotArray);
        }

        public IReadOnlyList<ProviderSnapshot> Snapshots { get; }
    }

    public sealed class Corrupt : SnapshotCacheReadResult
    {
        public Corrupt(string quarantineFileName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(quarantineFileName);
            if (!string.Equals(quarantineFileName, Path.GetFileName(quarantineFileName), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The quarantine value must contain a file name only.",
                    nameof(quarantineFileName));
            }

            QuarantineFileName = quarantineFileName;
        }

        public string QuarantineFileName { get; }
    }

    public sealed class UnsupportedVersion : SnapshotCacheReadResult
    {
        public UnsupportedVersion(int schemaVersion)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(schemaVersion, SnapshotStore.CurrentSchemaVersion);
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
    }
}

public abstract class SnapshotCacheSaveResult
{
    private SnapshotCacheSaveResult()
    {
    }

    public sealed class Saved : SnapshotCacheSaveResult
    {
        public Saved(IEnumerable<ProviderSnapshot> snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshots);
            ProviderSnapshot[] snapshotArray = snapshots.ToArray();
            if (snapshotArray.Any(snapshot => snapshot is null))
            {
                throw new ArgumentException("Snapshots cannot contain null values.", nameof(snapshots));
            }

            Snapshots = Array.AsReadOnly(snapshotArray);
        }

        public IReadOnlyList<ProviderSnapshot> Snapshots { get; }
    }

    public sealed class RefusedUnsupportedVersion : SnapshotCacheSaveResult
    {
        public RefusedUnsupportedVersion(int schemaVersion)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(schemaVersion, SnapshotStore.CurrentSchemaVersion);
            SchemaVersion = schemaVersion;
        }

        public int SchemaVersion { get; }
    }
}
