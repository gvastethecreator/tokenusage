#:property TargetFramework=net10.0
#:property Platform=x64
#:property Nullable=enable
#:property ImplicitUsings=enable
#:project ../src/TokenUsage.Core/TokenUsage.Core.csproj

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TokenUsage.Core.Providers;
using TokenUsage.Core.Usage;

string root = Path.Combine(Path.GetTempPath(), "tokenusage-ingest-bench", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string databasePath = Path.Combine(root, "usage.v1.db");
UsageRepository repository = await UsageRepository.OpenAsync(databasePath);
UsageEvent[] batch = Enumerable.Range(0, 10_000).Select(CreateEvent).ToArray();

long[] samples = new long[3];
for (int run = 0; run < samples.Length; run++)
{
    await repository.DeleteAllUsageDataAsync();
    var timer = Stopwatch.StartNew();
    UsageIngestResult result = await repository.IngestAsync(batch);
    timer.Stop();
    samples[run] = timer.ElapsedMilliseconds;
    Console.WriteLine($"run {run + 1}: {timer.ElapsedMilliseconds} ms inserted={result.InsertedCount} duplicates={result.DuplicateCount}");
}

Array.Sort(samples);
Console.WriteLine($"median: {samples[1]} ms");
Directory.Delete(root, recursive: true);

static UsageEvent CreateEvent(int index) =>
    new(
        new UsageEventKey(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"evt-{index}"))).ToLowerInvariant()),
        new AgentId("grok"),
        new ModelProviderId("xai"),
        new ModelId("grok-4.5"),
        new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero).AddSeconds(index),
        "Argentina Standard Time",
        new TokenBreakdown(100, 25, 5, 20, 0),
        CostObservation.ProviderReported(0.25m),
        "bench/1",
        CoverageKind.Complete);
