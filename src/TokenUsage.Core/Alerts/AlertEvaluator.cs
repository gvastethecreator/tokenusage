using TokenUsage.Core.Providers;

namespace TokenUsage.Core.Alerts;

public static class AlertEvaluator
{
    public static IReadOnlyList<AlertCandidate> Evaluate(
        AlertSettings settings,
        DateTimeOffset evaluatedAtUtc,
        IEnumerable<ProviderAlertFacts> providers)
    {
        ArgumentNullException.ThrowIfNull(settings);
        UtcTimestamp.Require(evaluatedAtUtc, nameof(evaluatedAtUtc));
        ArgumentNullException.ThrowIfNull(providers);

        ProviderAlertFacts[] providerArray = providers.ToArray();
        if (providerArray.Any(provider => provider is null))
        {
            throw new ArgumentException("Provider facts cannot contain null values.", nameof(providers));
        }

        string? duplicateProvider = providerArray
            .GroupBy(provider => provider.ProviderId.Value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateProvider is not null)
        {
            throw new ArgumentException(
                $"Provider ID '{duplicateProvider}' appears more than once.",
                nameof(providers));
        }

        var candidates = new List<AlertCandidate>();
        foreach (ProviderAlertFacts provider in providerArray)
        {
            AddQuotaThresholdCandidates(settings, evaluatedAtUtc, provider, candidates);
            AddExhaustionForecastCandidates(settings, evaluatedAtUtc, provider, candidates);
            AddProviderStateCandidates(settings, evaluatedAtUtc, provider, candidates);
        }

        return candidates.AsReadOnly();
    }

    private static void AddQuotaThresholdCandidates(
        AlertSettings settings,
        DateTimeOffset evaluatedAtUtc,
        ProviderAlertFacts provider,
        List<AlertCandidate> candidates)
    {
        if (!settings.IsEnabled(AlertKind.QuotaThreshold))
        {
            return;
        }

        foreach (QuotaAlertFacts quota in provider.Quotas)
        {
            if (quota.RemainingPercent > settings.QuotaThresholdPercent
                || quota.ResetsAtUtc is DateTimeOffset reset && reset <= evaluatedAtUtc)
            {
                continue;
            }

            var key = new AlertConditionKey(
                provider.ProviderId,
                quota.MetricId,
                AlertKind.QuotaThreshold,
                quota.ResetsAtUtc);
            candidates.Add(AlertCandidate.ForQuotaThreshold(
                key,
                evaluatedAtUtc,
                quota.RemainingPercent,
                settings.QuotaThresholdPercent));
        }
    }

    private static void AddExhaustionForecastCandidates(
        AlertSettings settings,
        DateTimeOffset evaluatedAtUtc,
        ProviderAlertFacts provider,
        List<AlertCandidate> candidates)
    {
        if (!settings.IsEnabled(AlertKind.ExhaustionForecast))
        {
            return;
        }

        foreach (QuotaAlertFacts quota in provider.Quotas)
        {
            if (quota.ProjectedExhaustionAtUtc is not DateTimeOffset exhaustion
                || quota.ResetsAtUtc is not DateTimeOffset reset
                || exhaustion <= evaluatedAtUtc
                || reset <= evaluatedAtUtc
                || exhaustion >= reset)
            {
                continue;
            }

            var key = new AlertConditionKey(
                provider.ProviderId,
                quota.MetricId,
                AlertKind.ExhaustionForecast,
                reset);
            candidates.Add(AlertCandidate.ForExhaustionForecast(
                key,
                evaluatedAtUtc,
                exhaustion));
        }
    }

    private static void AddProviderStateCandidates(
        AlertSettings settings,
        DateTimeOffset evaluatedAtUtc,
        ProviderAlertFacts provider,
        List<AlertCandidate> candidates)
    {
        if (provider.IsStale && settings.IsEnabled(AlertKind.StaleData))
        {
            candidates.Add(AlertCandidate.ForProviderState(
                new AlertConditionKey(
                    provider.ProviderId,
                    metricId: null,
                    AlertKind.StaleData,
                    quotaWindowResetsAtUtc: null),
                evaluatedAtUtc));
        }

        if (provider.HasCredentialFailure && settings.IsEnabled(AlertKind.CredentialFailure))
        {
            candidates.Add(AlertCandidate.ForProviderState(
                new AlertConditionKey(
                    provider.ProviderId,
                    metricId: null,
                    AlertKind.CredentialFailure,
                    quotaWindowResetsAtUtc: null),
                evaluatedAtUtc));
        }
    }
}
