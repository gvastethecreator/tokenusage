using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace WOpenUsage.Cli;

public static class ProvidersCommand
{
    public const string SchemaVersion = "wusage.providers.v1";
    public const string UsageText = "Usage: wusage providers [--format human|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The CLI boundary must redact all diagnostic failures.")]
    public static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter standardOutput,
        TextWriter standardError,
        ProviderDiagnosticsReader readDiagnostics,
        TimeProvider clock,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(readDiagnostics);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (!FormatOnlyCommandParser.TryParse(arguments, out CliOutputFormat format, out string error))
        {
            await standardError.WriteLineAsync(error).ConfigureAwait(false);
            await standardError.WriteLineAsync(UsageText).ConfigureAwait(false);
            return UsageCommand.InvalidUsageExitCode;
        }

        ProviderDiagnostic[] providers;
        try
        {
            ProviderDiagnosticsSnapshot snapshot = await readDiagnostics(cancellationToken)
                .ConfigureAwait(false);
            providers = ProviderDiagnosticsValidator.ValidateProviders(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await standardError.WriteLineAsync("Unable to detect providers.")
                .ConfigureAwait(false);
            return UsageCommand.NoDataExitCode;
        }

        DateTimeOffset generatedAt = TruncateToSecond(clock.GetUtcNow().ToUniversalTime());
        if (format == CliOutputFormat.Json)
        {
            var document = new ProvidersDocument(
                SchemaVersion,
                generatedAt,
                providers.Select(CreateProviderDocument).ToArray());
            await standardOutput.WriteLineAsync(JsonSerializer.Serialize(document, JsonOptions))
                .ConfigureAwait(false);
        }
        else
        {
            foreach (ProviderDiagnostic provider in providers)
            {
                string capabilities = string.Join(
                    ",",
                    provider.Capabilities.Select(ToLowerCamelCase));
                await standardOutput.WriteLineAsync(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{provider.Id}: {ToLowerCamelCase(provider.Detection)}; data {ToLowerCamelCase(provider.Data)}; {capabilities}"))
                    .ConfigureAwait(false);
            }
        }

        return UsageCommand.SuccessExitCode;
    }

    private static ProviderDocument CreateProviderDocument(ProviderDiagnostic provider) =>
        new(
            provider.Id,
            provider.Name,
            provider.Capabilities.Select(ToLowerCamelCase).ToArray(),
            ToLowerCamelCase(provider.Detection),
            ToLowerCamelCase(provider.Data));

    private static DateTimeOffset TruncateToSecond(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    private static string ToLowerCamelCase<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private sealed record ProvidersDocument(
        string SchemaVersion,
        DateTimeOffset GeneratedAt,
        IReadOnlyList<ProviderDocument> Providers);

    private sealed record ProviderDocument(
        string Id,
        string Name,
        IReadOnlyList<string> Capabilities,
        string Detection,
        string Data);
}
