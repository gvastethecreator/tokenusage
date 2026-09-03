using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Cli;

public static class PricingCommand
{
    public const string SchemaVersion = "tokenusage.pricing-audit.v1";
    public const string UsageText =
        "Usage: tokenusage pricing audit [--format human|json]\n"
        + "       tokenusage pricing refresh <--dry-run|--update> "
        + "[--source-root directory] [--output docs/pricing-refresh.md]";
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

        if (arguments.Count == 0)
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        if (arguments[0] == "refresh")
        {
            return await RunRefreshAsync(
                arguments.Skip(1).ToArray(),
                standardOutput,
                standardError,
                clock,
                refreshReader: null,
                Directory.GetCurrentDirectory(),
                cancellationToken).ConfigureAwait(false);
        }

        if (arguments[0] != "audit")
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

    internal static async Task<int> RunRefreshAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        TimeProvider clock,
        PricingRefreshSourceReader? refreshReader,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        if (!PricingRefreshOptions.TryParse(arguments, out PricingRefreshOptions options))
        {
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        string repositoryRoot = Path.GetFullPath(workingDirectory);
        PricingRefreshSourceReader reader;
        OfficialPricingSourceReader? officialReader = null;
        if (options.SourceRoot is not null)
        {
            string sourceRoot = ResolveWithinRoot(repositoryRoot, options.SourceRoot);
            reader = (definition, token) => ReadFixtureAsync(sourceRoot, definition, token);
        }
        else if (refreshReader is not null)
        {
            reader = refreshReader;
        }
        else
        {
            officialReader = new OfficialPricingSourceReader();
            reader = officialReader.ReadAsync;
        }

        try
        {
            var inputs = new List<PricingRefreshSourceInput>(PricingRefreshManifest.Sources.Count);
            foreach (PricingRefreshSourceDefinition definition in PricingRefreshManifest.Sources)
            {
                inputs.Add(await reader(definition, cancellationToken).ConfigureAwait(false));
            }

            DateTimeOffset nowUtc = clock.GetUtcNow().ToUniversalTime();
            PricingRefreshResult result = PricingRefreshEvaluator.Evaluate(
                PricingRefreshManifest.Sources,
                inputs,
                nowUtc);
            string report = PricingRefreshEvaluator.RenderMarkdown(result);
            if (!options.Update)
            {
                await standardOutput.WriteAsync(report).ConfigureAwait(false);
            }
            else
            {
                string outputPath = ResolveWithinRoot(repositoryRoot, options.OutputPath);
                bool changed = await WriteIfChangedAsync(outputPath, report, cancellationToken)
                    .ConfigureAwait(false);
                await standardOutput.WriteLineAsync(changed
                        ? "pricing-refresh: report updated"
                        : "pricing-refresh: no changes")
                    .ConfigureAwait(false);
            }

            return result.HasHardFailure
                ? UsageCommand.NoDataExitCode
                : UsageCommand.SuccessExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Unable to refresh pricing sources.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }
        finally
        {
            officialReader?.Dispose();
        }
    }

    private static async Task<PricingRefreshSourceInput> ReadFixtureAsync(
        string sourceRoot,
        PricingRefreshSourceDefinition definition,
        CancellationToken cancellationToken)
    {
        string path = ResolveWithinRoot(sourceRoot, definition.FixtureFileName);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return new(definition.Source.Id, PricingRefreshReadStatus.Unavailable, null);
            }

            if (file.Length > definition.MaximumBytes)
            {
                return new(definition.Source.Id, PricingRefreshReadStatus.Oversized, null);
            }

            string content = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            return new(definition.Source.Id, PricingRefreshReadStatus.Available, content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new(definition.Source.Id, PricingRefreshReadStatus.Unavailable, null);
        }
    }

    private static async Task<bool> WriteIfChangedAsync(
        string outputPath,
        string content,
        CancellationToken cancellationToken)
    {
        if (File.Exists(outputPath)
            && string.Equals(
                await File.ReadAllTextAsync(outputPath, cancellationToken).ConfigureAwait(false),
                content,
                StringComparison.Ordinal))
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = outputPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, content, cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporaryPath, outputPath, overwrite: true);
        return true;
    }

    private static string ResolveWithinRoot(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path, fullRoot);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The pricing path must stay inside the repository.");
        }

        return fullPath;
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

    private sealed record PricingRefreshOptions(
        bool Update,
        string? SourceRoot,
        string OutputPath)
    {
        public static bool TryParse(
            IReadOnlyList<string> arguments,
            out PricingRefreshOptions options)
        {
            options = null!;
            bool? update = null;
            string? sourceRoot = null;
            string outputPath = Path.Combine("docs", "pricing-refresh.md");
            for (int index = 0; index < arguments.Count; index++)
            {
                switch (arguments[index])
                {
                    case "--dry-run" when update is null:
                        update = false;
                        break;
                    case "--update" when update is null:
                        update = true;
                        break;
                    case "--source-root" when sourceRoot is null && index + 1 < arguments.Count:
                        sourceRoot = arguments[++index];
                        break;
                    case "--output" when index + 1 < arguments.Count:
                        outputPath = arguments[++index];
                        break;
                    default:
                        return false;
                }
            }

            if (update is null || string.IsNullOrWhiteSpace(outputPath)
                || (update == false && outputPath != Path.Combine("docs", "pricing-refresh.md")))
            {
                return false;
            }

            options = new(update.Value, sourceRoot, outputPath);
            return true;
        }
    }
}
