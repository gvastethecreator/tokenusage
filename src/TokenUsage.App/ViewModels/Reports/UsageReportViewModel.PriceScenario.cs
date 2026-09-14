using System.Globalization;
using TokenUsage.Core.Automation;
using TokenUsage.Core.Usage;
using TokenUsage.Providers.Codex;
using TokenUsage.Providers.Claude;
using TokenUsage.Providers.Cursor;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.App.ViewModels.Reports;

public sealed partial class UsageReportViewModel
{
    private bool _useLinearPriceScenario;
    private UsageLinearPriceResult? _linearPriceResult;
    public bool UseLinearPriceScenario
    {
        get => _useLinearPriceScenario;
        set
        {
            if (value == _useLinearPriceScenario || !CanChangeComparison || !IsComparePeriodsAxis) return;
            _useLinearPriceScenario = value;
            _linearPriceResult = null;
            if (value) _useReferencePrices = false;
            NotifyPriceScenarioOptions();
            _ = LoadAsync();
        }
    }

    private bool IsLinearPriceScenario => IsCompareScope && IsComparePeriodsAxis && UseLinearPriceScenario;
    public bool IsBaselineCatalogDateVisible => IsCompareRatesAxis || IsLinearPriceScenario;

    private void NotifyPriceScenarioOptions()
    {
        OnPropertyChanged(nameof(UseLinearPriceScenario));
        OnPropertyChanged(nameof(UseReferencePrices));
        OnPropertyChanged(nameof(IsReferencePriceOverlayVisible));
        OnPropertyChanged(nameof(IsCatalogDatePickerVisible));
        OnPropertyChanged(nameof(IsBaselineCatalogDateVisible));
    }

    private async Task ApplyLinearPriceScenarioAsync(CancellationToken token)
    {
        if (_compareLeftEnd < _compareLeftStart || _compareRightEnd < _compareRightStart)
        {
            _linearPriceResult = UsageLinearPriceScenario.Compare([], [], _rateBaselineUtc, _priceReferenceUtc, ResolveLinearTariff);
            return;
        }
        UsageHistoricalPriceScenario scenario = await Task.Run(() => new UsageReportQuery(_databasePath)
            .CompareLinearPricesAsync(_compareLeftStart, _compareLeftEnd, _compareRightStart, _compareRightEnd,
                _rateBaselineUtc, _priceReferenceUtc, ResolveLinearTariff,
                ComparisonConfigurationSelection(), ComparisonConfigurationSelection(), token), token);
        token.ThrowIfCancellationRequested();
        _report = scenario.Baseline;
        _compareRightReport = scenario.Current;
        _linearPriceResult = scenario.Result;
    }

    private static UsageLinearTariffResolution ResolveLinearTariff(UsageEvent row, DateTimeOffset atUtc) =>
        row.AgentId.Value is "codex" or "cursor" && row.ModelProviderId?.Value == "openai"
            ? CodexPricingCatalog.ResolveLinearTariff(row.ModelId.Value, atUtc)
            : row.AgentId.Value is "claude" or "cursor" && row.ModelProviderId?.Value == "anthropic"
                ? ClaudePricingCatalog.ResolveLinearTariff(KnownModelPricingCatalog.Canonicalize(row.ModelId.Value), row.Tokens, atUtc)
            : row.AgentId.Value == "cursor" && row.ModelProviderId?.Value is "google" or "xai"
                ? CursorPricingCatalog.ResolveFirstPartyLinearTariff(row.ModelId.Value, atUtc)
            : new(null, UsagePriceExclusion.MissingTariff);

    private void BuildPriceScenarioExplanation(List<string> text, List<string> evidence)
    {
        text.Add(GetString("UsageLinearScenarioTitle"));
        if (_linearPriceResult is not { } result) { text.Add(GetString("UsageExplanationNotSaved")); return; }
        if (result.MethodId != UsageLinearPriceScenario.MethodId || result.PolicyId != UsageLinearPriceScenario.PolicyId
            || result.RoundingPolicyId != UsageLinearPriceScenario.RoundingPolicyId)
        {
            text.Add(GetString("UsageExplanationUnknownVersion"));
            return;
        }
        string Amount(decimal? amount) => amount is null ? GetString("UsageReportCompareUnavailable")
            : "$" + amount.Value.ToString("0.000000", CultureInfo.CurrentCulture);
        text.Add(string.Format(CultureInfo.CurrentCulture, GetString("UsageLinearScenarioValues"),
            Amount(result.CostA), Amount(result.CostB), Amount(result.Volume), Amount(result.Mix), Amount(result.Price), Amount(result.RoundingResidual)));
        text.Add(string.Format(CultureInfo.CurrentCulture, GetString("UsageLinearScenarioCohort"),
            result.EligibleTokensA, result.EligibleRecordsA, result.EligibleTokensB, result.EligibleRecordsB));
        if (result.EffectStatus != UsagePriceEffectStatus.Available) text.Add(GetString("UsageLinearScenarioZeroVolume"));
        text.Add(GetString("UsageLinearScenarioHint"));
        evidence.Add(result.MethodId + " · " + result.PolicyId + " · " + result.RoundingPolicyId);
        evidence.Add("A: " + _compareLeftStart.ToString("O", CultureInfo.InvariantCulture) + " – " + _compareLeftEnd.ToString("O", CultureInfo.InvariantCulture)
            + "; B: " + _compareRightStart.ToString("O", CultureInfo.InvariantCulture) + " – " + _compareRightEnd.ToString("O", CultureInfo.InvariantCulture));
        evidence.Add(string.Format(CultureInfo.CurrentCulture, GetString("UsageLinearScenarioDates"),
            result.CatalogDateA.ToString("u", CultureInfo.InvariantCulture), result.CatalogDateB.ToString("u", CultureInfo.InvariantCulture)));
        foreach (UsagePriceExcluded excluded in result.Exclusions)
            text.Add(string.Format(CultureInfo.CurrentCulture, GetString("UsageLinearScenarioExcluded"), PriceExclusionText(excluded.Reason),
                excluded.TokensA, excluded.RecordsA, excluded.TokensB, excluded.RecordsB));
        evidence.Add(GetString("UsageLinearScenarioCells"));
        foreach (UsagePriceCellResult row in result.Cells)
            evidence.Add(string.Join(" · ", row.Cell.Agent, row.Cell.Host, row.Cell.Model, row.Cell.Tier, row.Cell.Effort ?? "?", row.Cell.Component,
                row.Cell.Regime, "A " + row.TokensA.ToString(CultureInfo.InvariantCulture), "B " + row.TokensB.ToString(CultureInfo.InvariantCulture),
                row.RateA.ToString(CultureInfo.InvariantCulture) + " / " + row.RateB.ToString(CultureInfo.InvariantCulture),
                row.CatalogA + "/" + row.PriceMatchA, row.CatalogB + "/" + row.PriceMatchB));
        evidence.Add("A: " + DescribeConfiguration(_report));
        evidence.Add("B: " + DescribeConfiguration(_compareRightReport));
        if (!HasSavedComparison) evidence.Add(_measurementEvidence);
    }

    private string PriceExclusionText(UsagePriceExclusion reason) => reason switch
    {
        UsagePriceExclusion.MissingTariff => GetString("UsageLinearMissingTariff"),
        UsagePriceExclusion.NonLinearRegime => GetString("UsageLinearNonLinear"),
        UsagePriceExclusion.UnknownConfiguration => GetString("UsageLinearUnknownConfiguration"),
        UsagePriceExclusion.UnknownComponents => GetString("UsageLinearUnknownComponents"),
        UsagePriceExclusion.UnknownDiscount => GetString("UsageLinearUnknownDiscount"),
        UsagePriceExclusion.MissingDetail => GetString("UsageLinearMissingDetail"),
        UsagePriceExclusion.IncompatibleRegime => GetString("UsageLinearIncompatibleRegime"),
        UsagePriceExclusion.IncompatibleMeasurement => GetString("UsageLinearIncompatibleMeasurement"),
        UsagePriceExclusion.UnsupportedTiming => GetString("UsageLinearUnsupportedTiming"),
        _ => GetString("UsageReportCompareUnavailable"),
    };
}
