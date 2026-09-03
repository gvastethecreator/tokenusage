using System.Security.Cryptography;
using System.Text;

namespace TokenUsage.Providers.Pricing;

public enum PricingRefreshReadStatus
{
    Available,
    Unavailable,
    Oversized,
    UnsupportedContentType,
}

public sealed record PricingRefreshSourceDefinition(
    PricingSourceEvidence Source,
    string FixtureFileName,
    int MaximumBytes,
    IReadOnlyList<string> RequiredMarkers);

public sealed record PricingRefreshSourceInput(
    string SourceId,
    PricingRefreshReadStatus Status,
    string? Content);

public sealed record PricingRefreshSourceCheck(
    string SourceId,
    PricingBillingScope BillingScope,
    PricingRefreshReadStatus ReadStatus,
    int MatchedMarkers,
    int TotalMarkers,
    string ProjectionSha256,
    string ExpectedProjectionSha256)
{
    public bool RequiresReview =>
        ReadStatus == PricingRefreshReadStatus.Available
        && MatchedMarkers != TotalMarkers;
}

public sealed record PricingRefreshResult(
    IReadOnlyList<PricingRefreshSourceCheck> SourceChecks,
    IReadOnlyList<PricingDiagnostic> CatalogDiagnostics)
{
    public bool HasHardFailure =>
        SourceChecks.Any(check => check.ReadStatus != PricingRefreshReadStatus.Available)
        || CatalogDiagnostics.Any(item =>
            item.Kind == PricingDiagnosticKind.ExpiredPromotionWithoutSuccessor);

    public bool RequiresReview =>
        SourceChecks.Any(check => check.RequiresReview)
        || CatalogDiagnostics.Any(item => item.Kind == PricingDiagnosticKind.StaleSource);
}

public static class PricingRefreshManifest
{
    public static IReadOnlyList<PricingRefreshSourceDefinition> Sources { get; } =
    [
        Define(
            PricingOfficialSources.Anthropic,
            "anthropic-model-pricing.html",
            ["Claude Sonnet 5", "$2", "$10", "will not occur"]),
        Define(
            PricingOfficialSources.Cursor,
            "cursor-model-pricing.html",
            ["Composer 2.5", "Grok 4.5", "Grok 4.6", "Fast"]),
        Define(
            PricingOfficialSources.CursorGemini,
            "cursor-gemini-3-8-pricing.html",
            ["Gemini 3.8 Flash", "$0.75", "$0.075", "$3.50"]),
        Define(
            PricingOfficialSources.Google,
            "google-gemini-api-pricing.html",
            ["Gemini 3.8 Flash", "$0.75", "$0.075", "$3.75", "December 31, 2026"]),
        Define(
            PricingOfficialSources.Moonshot,
            "moonshot-model-pricing.html",
            ["Kimi K3", "$3", "0.3", "15"]),
        Define(
            PricingOfficialSources.OpenAi,
            "openai-model-pricing.html",
            ["GPT-5.6 Sol", "$4.00", "$0.40", "$20.00", "November 21, 2026"]),
        Define(
            PricingOfficialSources.Xai,
            "xai-model-pricing.html",
            ["grok-4.6", "$2.00", "$0.50", "$6.00"]),
        Define(
            PricingOfficialSources.Zai,
            "zai-model-pricing.html",
            ["GLM-5.3-Flash", "$0.075", "$0.015", "$0.25", "September 9, 2026"]),
    ];

    private static PricingRefreshSourceDefinition Define(
        PricingSourceEvidence source,
        string fixtureFileName,
        IReadOnlyList<string> requiredMarkers) =>
        new(source, fixtureFileName, 1_048_576, requiredMarkers);
}

public static class PricingRefreshEvaluator
{
    public static PricingRefreshResult Evaluate(
        IReadOnlyList<PricingRefreshSourceDefinition> definitions,
        IReadOnlyList<PricingRefreshSourceInput> inputs,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(inputs);
        if (nowUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must use the UTC offset.", nameof(nowUtc));
        }

        if (definitions.Select(item => item.Source.Id).Distinct(StringComparer.Ordinal).Count()
            != definitions.Count)
        {
            throw new InvalidDataException("Pricing refresh source ids must be unique.");
        }

        var checks = new List<PricingRefreshSourceCheck>(definitions.Count);
        foreach (PricingRefreshSourceDefinition definition in definitions
                     .OrderBy(item => item.Source.Id, StringComparer.Ordinal))
        {
            PricingRefreshSourceInput input = inputs.SingleOrDefault(item =>
                    item.SourceId == definition.Source.Id)
                ?? new PricingRefreshSourceInput(
                    definition.Source.Id,
                    PricingRefreshReadStatus.Unavailable,
                    null);
            int[] actualProjection = definition.RequiredMarkers
                .Select(marker => input.Status == PricingRefreshReadStatus.Available
                                  && input.Content?.Contains(
                                      marker,
                                      StringComparison.OrdinalIgnoreCase) == true
                    ? 1
                    : 0)
                .ToArray();
            int[] expectedProjection = definition.RequiredMarkers.Select(_ => 1).ToArray();
            checks.Add(new(
                definition.Source.Id,
                definition.Source.BillingScope,
                input.Status,
                actualProjection.Sum(),
                actualProjection.Length,
                HashProjection(definition.RequiredMarkers, actualProjection),
                HashProjection(definition.RequiredMarkers, expectedProjection)));
        }

        IReadOnlyList<PricingDiagnostic> diagnostics = PricingCatalogAudit.Evaluate(
            PricingEvidenceCatalog.AllRates,
            nowUtc,
            TimeSpan.FromDays(45),
            TimeSpan.FromDays(30));
        return new(checks, diagnostics);
    }

    public static string RenderMarkdown(PricingRefreshResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new StringBuilder();
        builder.AppendLine("# Pricing refresh report");
        builder.AppendLine();
        builder.AppendLine(
            "Generated from bounded projections of allowlisted official pricing pages. "
            + "No fetched page content is stored.");
        builder.AppendLine();
        builder.AppendLine("## Source checks");
        builder.AppendLine();
        builder.AppendLine("| Source | Scope | Result | Projection |");
        builder.AppendLine("|---|---|---|---|");
        foreach (PricingRefreshSourceCheck check in result.SourceChecks)
        {
            string status = check.ReadStatus != PricingRefreshReadStatus.Available
                ? Camel(check.ReadStatus)
                : check.RequiresReview
                    ? $"review required ({check.MatchedMarkers}/{check.TotalMarkers} markers)"
                    : $"current ({check.TotalMarkers}/{check.TotalMarkers} markers)";
            builder.Append("| ").Append(check.SourceId)
                .Append(" | ").Append(Camel(check.BillingScope))
                .Append(" | ").Append(status)
                .Append(" | `").Append(check.ProjectionSha256[..12]).AppendLine("` |");
        }

        builder.AppendLine();
        builder.AppendLine("## Catalog candidates");
        builder.AppendLine();
        if (result.SourceChecks.Any(check => check.RequiresReview))
        {
            builder.AppendLine(
                "Unstructured source projections changed. Review the official pages and "
                + "prepare catalog edits manually; this refresh did not edit a price.");
        }
        else
        {
            builder.AppendLine("No catalog price change was detected by the supported projections.");
        }

        builder.AppendLine();
        builder.AppendLine("## Evidence candidates");
        builder.AppendLine();
        PricingDiagnostic[] stale = result.CatalogDiagnostics
            .Where(item => item.Kind == PricingDiagnosticKind.StaleSource)
            .ToArray();
        if (stale.Length == 0)
        {
            builder.AppendLine("No source review date needs an update.");
        }
        else
        {
            foreach (PricingDiagnostic diagnostic in stale)
            {
                builder.Append("- Review and re-date `")
                    .Append(diagnostic.SourceId)
                    .AppendLine("`.");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Scheduled transitions");
        builder.AppendLine();
        PricingDiagnostic[] promotions = result.CatalogDiagnostics
            .Where(item => item.Kind == PricingDiagnosticKind.PromotionNearExpiry)
            .ToArray();
        if (promotions.Length == 0)
        {
            builder.AppendLine("No promotion ends within 30 days.");
        }
        else
        {
            foreach (PricingDiagnostic promotion in promotions)
            {
                builder.Append("- `").Append(promotion.ExactPriceMatch)
                    .Append("` switches at ")
                    .Append(promotion.EffectiveUntilUtc!.Value.ToString("O"))
                    .AppendLine("; the successor rate is already versioned.");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Result");
        builder.AppendLine();
        builder.AppendLine(result.HasHardFailure
            ? "Failed: at least one source could not be checked or a promotion lacks a successor."
            : result.RequiresReview
                ? "Review required. The workflow may update one draft pull request; a human must edit, approve, and merge it."
                : "Current. No pull request is needed.");
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string HashProjection(
        IReadOnlyList<string> markers,
        int[] values)
    {
        string projection = string.Join(
            '\n',
            markers.Select((marker, index) => $"{marker}={values[index]}"));
        byte[] bytes = Encoding.UTF8.GetBytes(projection);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static string Camel<T>(T value) where T : struct, Enum =>
        char.ToLowerInvariant(value.ToString()[0]) + value.ToString()[1..];
}
