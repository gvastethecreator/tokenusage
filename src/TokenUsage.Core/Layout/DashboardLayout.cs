using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Layout;

/// <summary>
/// Immutable, ordered dashboard layout preferences for providers and their metrics.
/// </summary>
public sealed class DashboardLayout : IEquatable<DashboardLayout>
{
    public const int MaxProviders = 100;
    public const int MaxMetricsPerProvider = 100;
    public const int MaxHighlightedProviders = 4;
    public const int MaxHighlightedMetricsPerProvider = 2;

    public static DashboardLayout Empty { get; } = new(Array.Empty<ProviderLayoutPreference>());

    private readonly ProviderLayoutPreference[] _providers;
    private readonly IReadOnlyList<ProviderLayoutPreference> _providerView;

    public IReadOnlyList<ProviderLayoutPreference> Providers => _providerView;

    public DashboardLayout(IEnumerable<ProviderLayoutPreference> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var list = new List<ProviderLayoutPreference>();
        var seenProviders = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            if (provider is null)
            {
                throw new ArgumentException("Provider preferences must not contain null entries.", nameof(providers));
            }

            if (!seenProviders.Add(provider.ProviderId.Value))
            {
                throw new ArgumentException(
                    $"Duplicate provider id '{provider.ProviderId.Value}'.",
                    nameof(providers));
            }

            list.Add(provider);
        }

        if (list.Count > MaxProviders)
        {
            throw new ArgumentException(
                $"At most {MaxProviders} providers are allowed.",
                nameof(providers));
        }

        _providers = list.ToArray();
        _providerView = Array.AsReadOnly(_providers);
    }

    public DashboardLayout MoveProvider(ProviderId providerId, int offset)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ValidateOffset(offset);
        var index = IndexOfProvider(providerId);
        var target = ClampIndex(index + offset, _providers.Length);
        if (target == index)
        {
            return this;
        }

        var next = (ProviderLayoutPreference[])_providers.Clone();
        var item = next[index];
        if (target > index)
        {
            Array.Copy(next, index + 1, next, index, target - index);
        }
        else
        {
            Array.Copy(next, target, next, target + 1, index - target);
        }

        next[target] = item;
        return new DashboardLayout(next);
    }

    public DashboardLayout SetProviderVisible(ProviderId providerId, bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        var index = IndexOfProvider(providerId);
        var current = _providers[index];
        if (current.IsVisible == isVisible)
        {
            return this;
        }

        var next = (ProviderLayoutPreference[])_providers.Clone();
        next[index] = current.WithVisibility(isVisible);
        return new DashboardLayout(next);
    }

    public DashboardLayout SetProviderHighlighted(ProviderId providerId, bool isHighlighted)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        var index = IndexOfProvider(providerId);
        var current = _providers[index];
        if (current.IsHighlighted == isHighlighted)
        {
            return this;
        }

        if (isHighlighted
            && _providers.Count(provider => provider.IsHighlighted)
                >= MaxHighlightedProviders)
        {
            return this;
        }

        var next = (ProviderLayoutPreference[])_providers.Clone();
        next[index] = current.WithHighlighted(isHighlighted);
        return new DashboardLayout(next);
    }

    public DashboardLayout SetProviderColor(ProviderId providerId, string? colorHex)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        string? normalized = ProviderColorPreference.Normalize(colorHex);
        var index = IndexOfProvider(providerId);
        var current = _providers[index];
        if (string.Equals(current.ColorHex, normalized, StringComparison.Ordinal))
        {
            return this;
        }

        var next = (ProviderLayoutPreference[])_providers.Clone();
        next[index] = current.WithColor(normalized);
        return new DashboardLayout(next);
    }

    public DashboardLayout MoveMetric(ProviderId providerId, MetricId metricId, int offset)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ArgumentNullException.ThrowIfNull(metricId);
        ValidateOffset(offset);
        var providerIndex = IndexOfProvider(providerId);
        var provider = _providers[providerIndex];
        var metricIndex = provider.IndexOfMetric(metricId);
        var target = ClampIndex(metricIndex + offset, provider.Metrics.Count);
        if (target == metricIndex)
        {
            return this;
        }

        var metrics = provider.Metrics.ToArray();
        var item = metrics[metricIndex];
        if (target > metricIndex)
        {
            Array.Copy(metrics, metricIndex + 1, metrics, metricIndex, target - metricIndex);
        }
        else
        {
            Array.Copy(metrics, target, metrics, target + 1, metricIndex - target);
        }

        metrics[target] = item;

        var next = (ProviderLayoutPreference[])_providers.Clone();
        next[providerIndex] = provider.WithMetrics(metrics);
        return new DashboardLayout(next);
    }

    public DashboardLayout SetMetricVisible(ProviderId providerId, MetricId metricId, bool isVisible)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ArgumentNullException.ThrowIfNull(metricId);
        var providerIndex = IndexOfProvider(providerId);
        var provider = _providers[providerIndex];
        var metricIndex = provider.IndexOfMetric(metricId);
        var metric = provider.Metrics[metricIndex];
        if (metric.IsVisible == isVisible)
        {
            return this;
        }

        var metrics = provider.Metrics.ToArray();
        metrics[metricIndex] = metric.WithVisibility(isVisible);

        var next = (ProviderLayoutPreference[])_providers.Clone();
        next[providerIndex] = provider.WithMetrics(metrics);
        return new DashboardLayout(next);
    }

    public DashboardLayout SetMetricHighlighted(ProviderId providerId, MetricId metricId, bool isHighlighted)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ArgumentNullException.ThrowIfNull(metricId);
        var providerIndex = IndexOfProvider(providerId);
        var provider = _providers[providerIndex];
        var metricIndex = provider.IndexOfMetric(metricId);
        var metric = provider.Metrics[metricIndex];
        if (metric.IsHighlighted == isHighlighted)
        {
            return this;
        }

        // Never evict another metric: refuse to highlight when the per-provider cap is already full.
        if (isHighlighted)
        {
            var highlightedCount = 0;
            foreach (var candidate in provider.Metrics)
            {
                if (candidate.IsHighlighted)
                {
                    highlightedCount++;
                }
            }

            if (highlightedCount >= MaxHighlightedMetricsPerProvider)
            {
                return this;
            }
        }

        var metrics = provider.Metrics.ToArray();
        metrics[metricIndex] = metric.WithHighlighted(isHighlighted);

        var next = (ProviderLayoutPreference[])_providers.Clone();
        next[providerIndex] = provider.WithMetrics(metrics);
        return new DashboardLayout(next);
    }

    public DashboardLayout SetMetricOnDemand(ProviderId providerId, MetricId metricId, bool isOnDemand)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ArgumentNullException.ThrowIfNull(metricId);
        var providerIndex = IndexOfProvider(providerId);
        var provider = _providers[providerIndex];
        var metricIndex = provider.IndexOfMetric(metricId);
        var metric = provider.Metrics[metricIndex];
        if (metric.IsOnDemand == isOnDemand)
        {
            return this;
        }

        var metrics = provider.Metrics.ToArray();
        metrics[metricIndex] = metric.WithOnDemand(isOnDemand);

        var next = (ProviderLayoutPreference[])_providers.Clone();
        next[providerIndex] = provider.WithMetrics(metrics);
        return new DashboardLayout(next);
    }

    /// <summary>
    /// Reconciles this layout with an ordered catalog of providers and metrics.
    /// Maps each catalog metric to <see cref="MetricLayoutCatalogEntry"/> with
    /// <c>IsOnDemand=false</c> and delegates to the catalog-entry method.
    /// </summary>
    public DashboardLayout Reconcile(
        IReadOnlyList<ProviderId> catalogProviders,
        IReadOnlyDictionary<ProviderId, IReadOnlyList<MetricId>> catalogMetricsByProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogProviders);
        ArgumentNullException.ThrowIfNull(catalogMetricsByProvider);

        var mapped = new Dictionary<ProviderId, IReadOnlyList<MetricLayoutCatalogEntry>>();
        foreach (var pair in catalogMetricsByProvider)
        {
            if (pair.Value is null)
            {
                mapped[pair.Key] = null!;
                continue;
            }

            var entries = new List<MetricLayoutCatalogEntry>(pair.Value.Count);
            foreach (var metricId in pair.Value)
            {
                // Preserve null entries so the catalog-entry method can validate them.
                entries.Add(metricId is null
                    ? null!
                    : new MetricLayoutCatalogEntry(metricId, isOnDemand: false));
            }

            mapped[pair.Key] = entries;
        }

        return ReconcileWithMetricCatalog(catalogProviders, mapped);
    }

    /// <summary>
    /// Reconciles this layout with an ordered catalog of providers and metric entries.
    /// Preserves saved order, flags, and unknown saved items, then appends new catalog
    /// providers/metrics in catalog order with visible=true, highlighted=false, and the
    /// catalog <see cref="MetricLayoutCatalogEntry.IsOnDemand"/> value, up to layout limits.
    /// </summary>
    public DashboardLayout ReconcileWithMetricCatalog(
        IReadOnlyList<ProviderId> catalogProviders,
        IReadOnlyDictionary<ProviderId, IReadOnlyList<MetricLayoutCatalogEntry>> catalogMetricsByProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogProviders);
        ArgumentNullException.ThrowIfNull(catalogMetricsByProvider);

        var catalogProviderSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in catalogProviders)
        {
            if (id is null)
            {
                throw new ArgumentException(
                    "Catalog providers must not contain null entries.",
                    nameof(catalogProviders));
            }

            if (!catalogProviderSet.Add(id.Value))
            {
                throw new ArgumentException(
                    $"Duplicate catalog provider id '{id.Value}'.",
                    nameof(catalogProviders));
            }
        }

        var result = new List<ProviderLayoutPreference>(_providers.Length + catalogProviders.Count);
        var retainedProviderIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var saved in _providers)
        {
            retainedProviderIds.Add(saved.ProviderId.Value);
            catalogMetricsByProvider.TryGetValue(saved.ProviderId, out var catalogMetrics);
            catalogMetrics ??= Array.Empty<MetricLayoutCatalogEntry>();
            result.Add(ReconcileProviderMetrics(saved, catalogMetrics));
        }

        foreach (var catalogProviderId in catalogProviders)
        {
            if (retainedProviderIds.Contains(catalogProviderId.Value))
            {
                continue;
            }

            if (result.Count >= MaxProviders)
            {
                break;
            }

            catalogMetricsByProvider.TryGetValue(catalogProviderId, out var catalogMetrics);
            catalogMetrics ??= Array.Empty<MetricLayoutCatalogEntry>();

            var metrics = new List<MetricLayoutPreference>(catalogMetrics.Count);
            ValidateCatalogMetrics(catalogProviderId, catalogMetrics, nameof(catalogMetricsByProvider));
            foreach (var entry in catalogMetrics)
            {
                if (metrics.Count >= MaxMetricsPerProvider)
                {
                    break;
                }

                metrics.Add(new MetricLayoutPreference(
                    entry.MetricId,
                    isVisible: true,
                    isHighlighted: false,
                    isOnDemand: entry.IsOnDemand));
            }

            result.Add(new ProviderLayoutPreference(
                catalogProviderId,
                isVisible: true,
                isHighlighted: false,
                metrics));
        }

        return new DashboardLayout(result);
    }

    private static ProviderLayoutPreference ReconcileProviderMetrics(
        ProviderLayoutPreference saved,
        IReadOnlyList<MetricLayoutCatalogEntry> catalogMetrics)
    {
        ValidateCatalogMetrics(saved.ProviderId, catalogMetrics, nameof(catalogMetrics));

        var metrics = new List<MetricLayoutPreference>(saved.Metrics.Count + catalogMetrics.Count);
        var retained = new HashSet<string>(StringComparer.Ordinal);

        foreach (var metric in saved.Metrics)
        {
            retained.Add(metric.MetricId.Value);
            metrics.Add(metric);
        }

        foreach (var entry in catalogMetrics)
        {
            if (retained.Contains(entry.MetricId.Value))
            {
                continue;
            }

            if (metrics.Count >= MaxMetricsPerProvider)
            {
                break;
            }

            metrics.Add(new MetricLayoutPreference(
                entry.MetricId,
                isVisible: true,
                isHighlighted: false,
                isOnDemand: entry.IsOnDemand));
        }

        return saved.WithMetrics(metrics);
    }

    private static void ValidateCatalogMetrics(
        ProviderId providerId,
        IReadOnlyList<MetricLayoutCatalogEntry> catalogMetrics,
        string parameterName)
    {
        var catalogSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalogMetrics)
        {
            if (entry is null)
            {
                throw new ArgumentException(
                    "Catalog metrics must not contain null entries.",
                    parameterName);
            }

            if (!catalogSet.Add(entry.MetricId.Value))
            {
                throw new ArgumentException(
                    $"Duplicate catalog metric id '{entry.MetricId.Value}' for provider '{providerId.Value}'.",
                    parameterName);
            }
        }
    }

    private int IndexOfProvider(ProviderId providerId)
    {
        for (var i = 0; i < _providers.Length; i++)
        {
            if (string.Equals(_providers[i].ProviderId.Value, providerId.Value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new KeyNotFoundException($"Provider '{providerId.Value}' was not found in the layout.");
    }

    private static void ValidateOffset(int offset)
    {
        if (offset is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "Offset must be -1 or +1.");
        }
    }

    private static int ClampIndex(int index, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        if (index < 0)
        {
            return 0;
        }

        if (index >= count)
        {
            return count - 1;
        }

        return index;
    }

    public bool Equals(DashboardLayout? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_providers.Length != other._providers.Length)
        {
            return false;
        }

        for (var i = 0; i < _providers.Length; i++)
        {
            if (!_providers[i].Equals(other._providers[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is DashboardLayout other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var provider in _providers)
        {
            hash.Add(provider);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Immutable preference row for one provider and its ordered metrics.
/// </summary>
public sealed class ProviderLayoutPreference : IEquatable<ProviderLayoutPreference>
{
    private readonly MetricLayoutPreference[] _metrics;
    private readonly IReadOnlyList<MetricLayoutPreference> _metricView;

    public ProviderId ProviderId { get; }
    public bool IsVisible { get; }
    public bool IsHighlighted { get; }
    public string? ColorHex { get; }
    public IReadOnlyList<MetricLayoutPreference> Metrics => _metricView;

    public ProviderLayoutPreference(
        ProviderId providerId,
        bool isVisible,
        bool isHighlighted,
        IEnumerable<MetricLayoutPreference> metrics,
        string? colorHex = null)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ArgumentNullException.ThrowIfNull(metrics);
        if (string.IsNullOrEmpty(providerId.Value))
        {
            throw new ArgumentException("Provider id must be non-empty.", nameof(providerId));
        }

        var list = new List<MetricLayoutPreference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var metric in metrics)
        {
            if (metric is null)
            {
                throw new ArgumentException("Metric preferences must not contain null entries.", nameof(metrics));
            }

            if (!seen.Add(metric.MetricId.Value))
            {
                throw new ArgumentException(
                    $"Duplicate metric id '{metric.MetricId.Value}' for provider '{providerId.Value}'.",
                    nameof(metrics));
            }

            list.Add(metric);
        }

        if (list.Count > DashboardLayout.MaxMetricsPerProvider)
        {
            throw new ArgumentException(
                $"At most {DashboardLayout.MaxMetricsPerProvider} metrics are allowed per provider.",
                nameof(metrics));
        }

        ProviderId = providerId;
        IsVisible = isVisible;
        IsHighlighted = isHighlighted;
        ColorHex = ProviderColorPreference.Normalize(colorHex);
        _metrics = list.ToArray();
        _metricView = Array.AsReadOnly(_metrics);
    }

    internal int IndexOfMetric(MetricId metricId)
    {
        for (var i = 0; i < _metrics.Length; i++)
        {
            if (string.Equals(_metrics[i].MetricId.Value, metricId.Value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new KeyNotFoundException(
            $"Metric '{metricId.Value}' was not found for provider '{ProviderId.Value}'.");
    }

    internal ProviderLayoutPreference WithVisibility(bool isVisible) =>
        new(ProviderId, isVisible, IsHighlighted, _metrics, ColorHex);

    internal ProviderLayoutPreference WithHighlighted(bool isHighlighted) =>
        new(ProviderId, IsVisible, isHighlighted, _metrics, ColorHex);

    internal ProviderLayoutPreference WithColor(string? colorHex) =>
        new(ProviderId, IsVisible, IsHighlighted, _metrics, colorHex);

    internal ProviderLayoutPreference WithMetrics(IEnumerable<MetricLayoutPreference> metrics) =>
        new(ProviderId, IsVisible, IsHighlighted, metrics, ColorHex);

    public bool Equals(ProviderLayoutPreference? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (!string.Equals(ProviderId.Value, other.ProviderId.Value, StringComparison.Ordinal)
            || IsVisible != other.IsVisible
            || IsHighlighted != other.IsHighlighted
            || !string.Equals(ColorHex, other.ColorHex, StringComparison.Ordinal)
            || _metrics.Length != other._metrics.Length)
        {
            return false;
        }

        for (var i = 0; i < _metrics.Length; i++)
        {
            if (!_metrics[i].Equals(other._metrics[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is ProviderLayoutPreference other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ProviderId.Value, StringComparer.Ordinal);
        hash.Add(IsVisible);
        hash.Add(IsHighlighted);
        hash.Add(ColorHex, StringComparer.Ordinal);
        foreach (var metric in _metrics)
        {
            hash.Add(metric);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// Immutable preference row for one metric within a provider.
/// </summary>
public sealed class MetricLayoutPreference : IEquatable<MetricLayoutPreference>
{
    public MetricId MetricId { get; }
    public bool IsVisible { get; }
    public bool IsHighlighted { get; }
    public bool IsOnDemand { get; }

    public MetricLayoutPreference(
        MetricId metricId,
        bool isVisible,
        bool isHighlighted,
        bool isOnDemand = false)
    {
        ArgumentNullException.ThrowIfNull(metricId);
        if (string.IsNullOrEmpty(metricId.Value))
        {
            throw new ArgumentException("Metric id must be non-empty.", nameof(metricId));
        }

        MetricId = metricId;
        IsVisible = isVisible;
        IsHighlighted = isHighlighted;
        IsOnDemand = isOnDemand;
    }

    internal MetricLayoutPreference WithVisibility(bool isVisible) =>
        new(MetricId, isVisible, IsHighlighted, IsOnDemand);

    internal MetricLayoutPreference WithHighlighted(bool isHighlighted) =>
        new(MetricId, IsVisible, isHighlighted, IsOnDemand);

    internal MetricLayoutPreference WithOnDemand(bool isOnDemand) =>
        new(MetricId, IsVisible, IsHighlighted, isOnDemand);

    public bool Equals(MetricLayoutPreference? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(MetricId.Value, other.MetricId.Value, StringComparison.Ordinal)
            && IsVisible == other.IsVisible
            && IsHighlighted == other.IsHighlighted
            && IsOnDemand == other.IsOnDemand;
    }

    public override bool Equals(object? obj) => obj is MetricLayoutPreference other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MetricId.Value, StringComparer.Ordinal);
        hash.Add(IsVisible);
        hash.Add(IsHighlighted);
        hash.Add(IsOnDemand);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Immutable catalog row describing one known metric for layout reconciliation.
/// </summary>
public sealed class MetricLayoutCatalogEntry
{
    public MetricId MetricId { get; }
    public bool IsOnDemand { get; }

    public MetricLayoutCatalogEntry(MetricId metricId, bool isOnDemand)
    {
        ArgumentNullException.ThrowIfNull(metricId);
        MetricId = metricId;
        IsOnDemand = isOnDemand;
    }
}
