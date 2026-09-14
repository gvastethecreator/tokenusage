using TokenUsage.Core.Automation;

namespace TokenUsage.Core.Usage;

public enum UsagePriceExclusion
{
    MissingTariff, NonLinearRegime, UnknownConfiguration, UnknownComponents,
    UnknownDiscount, MissingDetail, IncompatibleRegime, IncompatibleMeasurement, UnsupportedTiming,
}

/// <summary>Explicit catalog rates per million disjoint tokens; never inferred from observed cost.</summary>
public sealed record UsageLinearTariff(string CatalogVersion, string PriceMatch, string Regime,
    decimal Input, decimal Output, decimal Reasoning, decimal CacheRead, decimal CacheWrite);

public sealed record UsageLinearTariffResolution(UsageLinearTariff? Tariff, UsagePriceExclusion? Exclusion);

public sealed record UsagePriceCell(string Agent, string Host, string Model, string Tier,
    string? Effort, string Component, string Regime);

public sealed record UsagePriceCellResult(UsagePriceCell Cell, long TokensA, long TokensB,
    decimal RateA, decimal RateB, string CatalogA, string CatalogB, string PriceMatchA, string PriceMatchB);

public sealed record UsagePriceExcluded(UsagePriceExclusion Reason, long TokensA, long TokensB,
    int RecordsA, int RecordsB);

public enum UsagePriceEffectStatus { Available, ZeroVolumeBaseline, ZeroVolumeCurrent, ZeroVolumeBoth }

public sealed record UsageHistoricalPriceScenario(UsageReport Baseline, UsageReport Current,
    UsageLinearPriceResult Result);

public sealed record UsageLinearPriceResult(string MethodId, string PolicyId, string RoundingPolicyId,
    DateTimeOffset CatalogDateA, DateTimeOffset CatalogDateB,
    long EligibleTokensA, long EligibleTokensB, int EligibleRecordsA, int EligibleRecordsB,
    decimal? CostA, decimal? CostB, decimal? Volume, decimal? Mix, decimal? Price,
    decimal? RoundingResidual, UsagePriceEffectStatus EffectStatus, IReadOnlyList<UsagePriceCellResult> Cells,
    IReadOnlyList<UsagePriceExcluded> Exclusions);

public static class UsageLinearPriceScenario
{
    public const string MethodId = "sequential-vmp-linear/v1";
    public const string PolicyId = "explicit-standard-measured-components-list-rates/v1";
    public const string RoundingPolicyId = "usd-6-away-from-zero-separate-residual/v1";

    /// <summary>Inputs must be the selected retained observations from one coherent read.</summary>
    public static UsageLinearPriceResult Compare(IReadOnlyList<UsageEvent> baseline,
        IReadOnlyList<UsageEvent> current, DateTimeOffset dateA, DateTimeOffset dateB,
        Func<UsageEvent, DateTimeOffset, UsageLinearTariffResolution> resolve)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(resolve);
        if (dateA.Offset != TimeSpan.Zero || dateB.Offset != TimeSpan.Zero)
            throw new ArgumentException("Catalog dates must use UTC.");
        var cells = new Dictionary<UsagePriceCell, UsagePriceCellResult>();
        var exclusions = new Dictionary<UsagePriceExclusion, UsagePriceExcluded>();
        var incompatible = baseline.Concat(current).Where(row => row.DetailMetadata.RepresentationRevision is not null).GroupBy(row =>
                (row.AgentId, row.ModelProviderId, row.ModelId, row.ServiceTier, row.ReasoningEffort))
            .Where(group => group.Select(row => (row.ParserVersion, row.DetailMetadata.RepresentationRevision)).Distinct().Count() > 1)
            .Select(group => group.Key).ToHashSet();
        int recordsA = 0, recordsB = 0;
        Add(baseline, false);
        Add(current, true);
        UsagePriceCellResult[] ordered = cells.Values.OrderBy(row => row.Cell.Agent, StringComparer.Ordinal)
            .ThenBy(row => row.Cell.Host, StringComparer.Ordinal).ThenBy(row => row.Cell.Model, StringComparer.Ordinal)
            .ThenBy(row => row.Cell.Tier, StringComparer.Ordinal).ThenBy(row => row.Cell.Effort, StringComparer.Ordinal)
            .ThenBy(row => row.Cell.Component, StringComparer.Ordinal).ThenBy(row => row.Cell.Regime, StringComparer.Ordinal).ToArray();
        long quantityA = ordered.Sum(row => row.TokensA), quantityB = ordered.Sum(row => row.TokensB);
        decimal a = ordered.Sum(row => row.TokensA * row.RateA / 1_000_000m);
        decimal b = ordered.Sum(row => row.TokensB * row.RateB / 1_000_000m);
        decimal? costA = recordsA > 0 ? Round(a) : null, costB = recordsB > 0 ? Round(b) : null;
        decimal? volume = null, mix = null, price = null, residual = null;
        if (quantityA > 0 && quantityB > 0)
        {
            decimal bAtA = ordered.Sum(row => row.TokensB * row.RateA / 1_000_000m);
            decimal scaledA = a * quantityB / quantityA;
            costA = Round(a); costB = Round(b);
            volume = Round(scaledA - a);
            mix = Round(bAtA - scaledA);
            price = Round(b - bAtA);
            residual = costB - costA - volume - mix - price;
        }
        return new(MethodId, PolicyId, RoundingPolicyId, dateA, dateB, quantityA, quantityB,
            recordsA, recordsB, costA, costB, volume, mix, price, residual,
            quantityA == 0 ? quantityB == 0 ? UsagePriceEffectStatus.ZeroVolumeBoth : UsagePriceEffectStatus.ZeroVolumeBaseline
                : quantityB == 0 ? UsagePriceEffectStatus.ZeroVolumeCurrent : UsagePriceEffectStatus.Available, ordered,
            exclusions.Values.OrderBy(row => row.Reason).ToArray());

        void Add(IReadOnlyList<UsageEvent> observations, bool right)
        {
            foreach (UsageEvent row in observations)
            {
                UsageDetailMetadata detail = row.DetailMetadata;
                UsagePriceExclusion? reason = row.ModelProviderId is null || row.ServiceTier != "standard"
                    ? UsagePriceExclusion.UnknownConfiguration
                    : row.TimePrecision is not (UsageTimePrecision.Timestamp or UsageTimePrecision.Interval)
                        || detail.RecordKind is UsageRecordKind.Snapshot or UsageRecordKind.DailyAggregate
                        ? UsagePriceExclusion.UnsupportedTiming
                    : row.Cost.Kind == CostKind.ProviderReported ? UsagePriceExclusion.UnknownDiscount
                    : incompatible.Contains((row.AgentId, row.ModelProviderId, row.ModelId, row.ServiceTier, row.ReasoningEffort))
                        ? UsagePriceExclusion.IncompatibleMeasurement
                    : detail.RepresentationRevision is null || detail.Input != UsageComponentAvailability.Measured || detail.Output != UsageComponentAvailability.Measured
                        || detail.Reasoning != UsageComponentAvailability.Measured || detail.CacheRead != UsageComponentAvailability.Measured
                        || detail.CacheWrite != UsageComponentAvailability.Measured ? UsagePriceExclusion.UnknownComponents : null;
                UsageLinearTariff? tariffA = null, tariffB = null;
                if (reason is null)
                {
                    UsageLinearTariffResolution left = resolve(row, dateA), rightRate = resolve(row, dateB);
                    tariffA = left.Tariff; tariffB = rightRate.Tariff;
                    reason = left.Exclusion ?? rightRate.Exclusion;
                    if (reason is null && (tariffA is null || tariffB is null)) reason = UsagePriceExclusion.MissingTariff;
                    if (reason is null && tariffA!.Regime != tariffB!.Regime) reason = UsagePriceExclusion.IncompatibleRegime;
                }
                if (reason is { } excluded)
                {
                    exclusions.TryGetValue(excluded, out UsagePriceExcluded? previous);
                    previous ??= new(excluded, 0, 0, 0, 0);
                    exclusions[excluded] = right
                        ? previous with { TokensB = checked(previous.TokensB + row.Tokens.Total), RecordsB = checked(previous.RecordsB + 1) }
                        : previous with { TokensA = checked(previous.TokensA + row.Tokens.Total), RecordsA = checked(previous.RecordsA + 1) };
                    continue;
                }
                if (right) recordsB++; else recordsA++;
                AddComponent("input", row.Tokens.Input, tariffA!.Input, tariffB!.Input);
                AddComponent("output", row.Tokens.Output, tariffA.Output, tariffB.Output);
                AddComponent("reasoning", row.Tokens.Reasoning, tariffA.Reasoning, tariffB.Reasoning);
                AddComponent("cache-read", row.Tokens.CacheRead, tariffA.CacheRead, tariffB.CacheRead);
                AddComponent("cache-write", row.Tokens.CacheWrite, tariffA.CacheWrite, tariffB.CacheWrite);

                void AddComponent(string component, long tokens, decimal rateA, decimal rateB)
                {
                    if (rateA < 0 || rateB < 0) throw new ArgumentException("Linear tariffs must be nonnegative.");
                    if (tokens == 0) return;
                    var key = new UsagePriceCell(row.AgentId.Value, row.ModelProviderId!.Value,
                        row.ModelId.Value, row.ServiceTier!, row.ReasoningEffort, component, tariffA!.Regime);
                    if (!cells.TryGetValue(key, out UsagePriceCellResult? previous))
                        previous = new(key, 0, 0, rateA, rateB, tariffA.CatalogVersion, tariffB!.CatalogVersion,
                            tariffA.PriceMatch, tariffB.PriceMatch);
                    if (previous.RateA != rateA || previous.RateB != rateB || previous.CatalogA != tariffA.CatalogVersion
                        || previous.CatalogB != tariffB!.CatalogVersion || previous.PriceMatchA != tariffA.PriceMatch
                        || previous.PriceMatchB != tariffB.PriceMatch)
                        throw new InvalidOperationException("A linear cell resolved to inconsistent tariffs.");
                    cells[key] = right ? previous with { TokensB = checked(previous.TokensB + tokens) }
                        : previous with { TokensA = checked(previous.TokensA + tokens) };
                }
            }
        }
    }

    private static decimal Round(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
