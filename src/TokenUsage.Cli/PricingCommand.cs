using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Cli;

public static class PricingCommand
{
    public const string SchemaVersion = "tokenusage.pricing-audit.v1";
    public const string UsageText = "Usage: tokenusage pricing audit [--format human|json]";
    private static readonly TimeSpan SourceStaleAfter = TimeSpan.FromDays(45);
    private static readonly TimeSpan PromotionWarningWindow = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary returns a stable error without exposing local details.")]
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Count == 0 || arguments[0] != "audit")
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        if (!FormatOnlyCommandParser.TryParse(
                arguments.Skip(1).ToArray(),
                out CliOutputFormat format,
                out string error))
        {
            await standardError.WriteLineAsync(error).ConfigureAwait(false);
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        try
        {
            DateTimeOffset generatedAt = clock.GetUtcNow().ToUniversalTime();
            generatedAt = generatedAt.AddTicks(-(generatedAt.Ticks % TimeSpan.TicksPerSecond));
            IReadOnlyList<PricingRateEvidence> rates = PricingEvidenceCatalog.AllRates;
            IReadOnlyList<PricingDiagnostic> diagnostics = PricingCatalogAudit.Evaluate(
                rates,
                generatedAt,
                SourceStaleAfter,
                PromotionWarningWindow);

            if (format == CliOutputFormat.Json)
            {
                var document = new PricingAuditDocument(
                    SchemaVersion,
                    generatedAt,
                    PricingEvidenceCatalog.AllSources.Select(source => new SourceDocument(
                        source.Id,
                        source.OfficialUri.AbsoluteUri,
                        source.ReviewedOn,
                        JsonNamingPolicy.CamelCase.ConvertName(source.BillingScope.ToString())))
                        .ToArray(),
                    diagnostics.Select(item => new DiagnosticDocument(
                        JsonNamingPolicy.CamelCase.ConvertName(item.Kind.ToString()),
                        item.SourceId,
                        item.ExactPriceMatch,
                        item.EffectiveUntilUtc))
                        .ToArray());
                await standardOutput.WriteLineAsync(JsonSerializer.Serialize(document, JsonOptions))
                    .ConfigureAwait(false);
            }
            else
            {
                await standardOutput.WriteLineAsync(
                    $"pricing-catalog: valid; {rates.Count} evidenced rates; "
                    + $"{PricingEvidenceCatalog.AllSources.Count} official sources")
                    .ConfigureAwait(false);
                foreach (PricingDiagnostic diagnostic in diagnostics)
                {
                    string exactMatch = diagnostic.ExactPriceMatch is null
                        ? string.Empty
                        : $"; {diagnostic.ExactPriceMatch}";
                    string end = diagnostic.EffectiveUntilUtc is null
                        ? string.Empty
                        : $"; ends {diagnostic.EffectiveUntilUtc.Value:O}";
                    await standardOutput.WriteLineAsync(
                        $"{JsonNamingPolicy.CamelCase.ConvertName(diagnostic.Kind.ToString())}: "
                        + $"{diagnostic.SourceId}{exactMatch}{end}")
                        .ConfigureAwait(false);
                }
            }

            return diagnostics.Any(item =>
                item.Kind == PricingDiagnosticKind.ExpiredPromotionWithoutSuccessor)
                ? UsageCommand.NoDataExitCode
                : UsageCommand.SuccessExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Unable to audit pricing evidence.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }
    }

    private sealed record PricingAuditDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<SourceDocument> Sources,
        IReadOnlyList<DiagnosticDocument> Diagnostics);

    private sealed record SourceDocument(
        string Id,
        string OfficialUrl,
        DateOnly ReviewedOn,
        string BillingScope);

    private sealed record DiagnosticDocument(
        string Kind,
        string SourceId,
        string? ExactPriceMatch,
        DateTimeOffset? EffectiveUntilUtc);
}
