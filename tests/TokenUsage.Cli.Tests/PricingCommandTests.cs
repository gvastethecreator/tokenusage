using System.Globalization;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Cli.Tests;

public sealed class PricingCommandTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuditListsOfficialSourcesAndUpcomingPromotion()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await PricingCommand.RunAsync(
            ["audit", "--format", "json"],
            output,
            TextWriter.Null,
            new FixedTimeProvider(Now));

        Assert.Equal(0, exitCode);
        Assert.Contains("\"schemaVersion\": \"tokenusage.pricing-audit.v1\"", output.ToString());
        Assert.Contains("\"sourceId\": \"zai-model-pricing\"", output.ToString());
        Assert.Contains("\"kind\": \"promotionNearExpiry\"", output.ToString());
        Assert.DoesNotContain("ExpiredPromotionWithoutSuccessor", output.ToString());
    }

    [Theory]
    [InlineData()]
    [InlineData("refresh")]
    [InlineData("audit", "--format", "xml")]
    public async Task InvalidArgumentsReturnTwo(params string[] arguments)
    {
        var error = new StringWriter(CultureInfo.InvariantCulture);

        int exitCode = await PricingCommand.RunAsync(
            arguments,
            TextWriter.Null,
            error,
            new FixedTimeProvider(Now));

        Assert.Equal(2, exitCode);
        Assert.Contains(PricingCommand.UsageText, error.ToString());
    }

    [Fact]
    public async Task SavedFixturesProduceTheSameDryRunReport()
    {
        string repositoryRoot = FindRepositoryRoot();
        string fixtureRoot = Path.Combine("tests", "fixtures", "pricing");
        var first = new StringWriter(CultureInfo.InvariantCulture);
        var second = new StringWriter(CultureInfo.InvariantCulture);

        int firstExit = await PricingCommand.RunRefreshAsync(
            ["--dry-run", "--source-root", fixtureRoot],
            first,
            TextWriter.Null,
            new FixedTimeProvider(Now),
            refreshReader: null,
            repositoryRoot);
        int secondExit = await PricingCommand.RunRefreshAsync(
            ["--dry-run", "--source-root", fixtureRoot],
            second,
            TextWriter.Null,
            new FixedTimeProvider(Now),
            refreshReader: null,
            repositoryRoot);

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.Equal(first.ToString(), second.ToString());
        Assert.Contains("Current. No pull request is needed.", first.ToString());
        Assert.Contains("`glm-5.3-flash` switches", first.ToString());
    }

    [Fact]
    public async Task UpdateWritesOnlyAReviewReportWhenHtmlProjectionChanges()
    {
        string root = CreateFixtureRoot(changeOpenAiProjection: true);
        try
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);

            int exitCode = await PricingCommand.RunRefreshAsync(
                ["--update", "--source-root", "sources"],
                output,
                TextWriter.Null,
                new FixedTimeProvider(Now),
                refreshReader: null,
                root);

            Assert.Equal(0, exitCode);
            Assert.Contains("report updated", output.ToString());
            string report = await File.ReadAllTextAsync(
                Path.Combine(root, "docs", "pricing-refresh.md"));
            Assert.Contains("review required", report);
            Assert.Contains("this refresh did not edit a price", report);
            Assert.Empty(Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UpdateIsStableWhenTheGeneratedReportHasNotChanged()
    {
        string root = CreateFixtureRoot(changeOpenAiProjection: false);
        try
        {
            int first = await PricingCommand.RunRefreshAsync(
                ["--update", "--source-root", "sources"],
                TextWriter.Null,
                TextWriter.Null,
                new FixedTimeProvider(Now),
                refreshReader: null,
                root);
            var secondOutput = new StringWriter(CultureInfo.InvariantCulture);
            int second = await PricingCommand.RunRefreshAsync(
                ["--update", "--source-root", "sources"],
                secondOutput,
                TextWriter.Null,
                new FixedTimeProvider(Now),
                refreshReader: null,
                root);

            Assert.Equal(0, first);
            Assert.Equal(0, second);
            Assert.Contains("no changes", secondOutput.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OversizedSourceFailsWithoutWritingFetchedContent()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        PricingRefreshSourceReader oversized = (definition, _) =>
            Task.FromResult(new PricingRefreshSourceInput(
                definition.Source.Id,
                PricingRefreshReadStatus.Oversized,
                null));

        int exitCode = await PricingCommand.RunRefreshAsync(
            ["--dry-run"],
            output,
            TextWriter.Null,
            new FixedTimeProvider(Now),
            oversized,
            FindRepositoryRoot());

        Assert.Equal(4, exitCode);
        Assert.Contains("Failed: at least one source could not be checked", output.ToString());
        Assert.DoesNotContain("<!doctype", output.ToString());
    }

    [Fact]
    public void RefreshManifestUsesOnlyTypedOfficialSourcesAndBoundedReads()
    {
        Assert.Equal(
            PricingEvidenceCatalog.AllSources.Select(source => source.Id).Order(),
            PricingRefreshManifest.Sources.Select(source => source.Source.Id).Order());
        Assert.All(PricingRefreshManifest.Sources, source =>
        {
            Assert.Equal(Uri.UriSchemeHttps, source.Source.OfficialUri.Scheme);
            Assert.InRange(source.MaximumBytes, 1, 1_048_576);
            Assert.NotEmpty(source.RequiredMarkers);
        });
    }

    private static string CreateFixtureRoot(bool changeOpenAiProjection)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "tokenusage-pricing-refresh-" + Guid.NewGuid().ToString("N"));
        string sourceRoot = Path.Combine(root, "sources");
        Directory.CreateDirectory(sourceRoot);
        foreach (PricingRefreshSourceDefinition definition in PricingRefreshManifest.Sources)
        {
            string content = string.Join(' ', definition.RequiredMarkers);
            if (changeOpenAiProjection && definition.Source.Id == "openai-model-pricing")
            {
                content = "Pricing page with a changed unsupported structure.";
            }

            File.WriteAllText(Path.Combine(sourceRoot, definition.FixtureFileName), content);
        }

        return root;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TokenUsage.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("TokenUsage repository root was not found.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
