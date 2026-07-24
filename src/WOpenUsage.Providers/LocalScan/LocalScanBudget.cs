namespace WOpenUsage.Providers.LocalScan;

/// <summary>
/// Shared scan budget and partial-scan tracking for local usage sources.
/// Parsers stay provider-specific; only limits and diagnostics helpers are shared.
/// </summary>
public sealed class LocalScanBudget
{
    public LocalScanBudget(
        int maximumFiles = 10_000,
        long maximumFileBytes = 64 * 1024 * 1024,
        int maximumLineBytes = 8 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFiles, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLineBytes, 1);
        MaximumFiles = maximumFiles;
        MaximumFileBytes = maximumFileBytes;
        MaximumLineBytes = maximumLineBytes;
    }

    public int MaximumFiles { get; }

    public long MaximumFileBytes { get; }

    public int MaximumLineBytes { get; }
}

public sealed class LocalScanState
{
    private readonly LocalScanBudget _budget;

    public LocalScanState(LocalScanBudget budget)
    {
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
    }

    public int FilesRead { get; set; }

    public bool IsPartial { get; set; }

    public bool UnsupportedSchema { get; set; }

    public bool TryConsumeFile()
    {
        FilesRead++;
        if (FilesRead > _budget.MaximumFiles)
        {
            IsPartial = true;
            return false;
        }

        return true;
    }

    public bool IsFileTooLarge(long length)
    {
        if (length > _budget.MaximumFileBytes)
        {
            IsPartial = true;
            return true;
        }

        return false;
    }

    public void MarkPartial() => IsPartial = true;

    public int MaximumLineBytes => _budget.MaximumLineBytes;
}
