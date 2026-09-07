using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.Json;
using TokenUsage.Core.Updates;

namespace TokenUsage.Core.Tests.Updates;

public sealed class PortableUpdateInstallerTests
{
    private static readonly string[] ReleaseMetadataFiles = ["TokenUsage.portable", "TokenUsage.files.json"];
    [Fact]
    public async Task InstallsAfterOwningProcessExitsAndPreservesDataUnownedFilesAndBackup()
    {
        using var folder = new PortableFolder();
        string unchangedProduct = Path.Combine(folder.Install, "TokenUsage.App.dll");
        DateTime unchangedTimestamp = File.GetLastWriteTimeUtc(unchangedProduct);
        await File.WriteAllTextAsync(Path.Combine(folder.Install, "a-change.txt"), "old");
        folder.AddOwnedFile("a-change.txt");
        await File.WriteAllTextAsync(Path.Combine(folder.Install, "obsolete.txt"), "old component");
        folder.AddOwnedFile("obsolete.txt");
        await File.WriteAllTextAsync(Path.Combine(folder.Install, "personal.txt"), "keep");
        Directory.CreateDirectory(Path.Combine(folder.Install, "Data"));
        await File.WriteAllTextAsync(Path.Combine(folder.Install, "Data", "usage.db"), "private data");
        string archive = folder.CreateArchive(("a-change.txt", "new"));
        PortableUpdatePlan plan = await folder.PrepareAsync(archive);
        await SimulateExitedParentAsync(plan);

        int result = await PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId);

        Assert.Equal(0, result);
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(folder.Install, "a-change.txt")));
        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(folder.Install, "personal.txt")));
        Assert.Equal("private data", await File.ReadAllTextAsync(Path.Combine(folder.Install, "Data", "usage.db")));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(plan.PlanPath)!, "backup", "a-change.txt")));
        Assert.Equal("installed", (await PortableUpdateInstaller.ReadResultAsync(plan.PlanPath))?.Status);
        Assert.False(File.Exists(Path.Combine(folder.Install, "obsolete.txt")));
        Assert.Equal("old component", await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(plan.PlanPath)!, "backup", "obsolete.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(plan.PlanPath)!, "backup", "TokenUsage.App.dll")));
        Assert.Equal(unchangedTimestamp, File.GetLastWriteTimeUtc(unchangedProduct));
        await PortableUpdateInstaller.CleanupAfterSuccessfulInstallAsync(plan.PlanPath);
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(plan.PlanPath)!, "stage")));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(plan.PlanPath)!, "worker")));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(plan.PlanPath)!, "backup", "a-change.txt")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("Data/usage.db")]
    [InlineData("payload.txt:stream")]
    [InlineData("CON.txt")]
    public async Task RejectsUnsafeArchiveWithoutChangingInstallation(string unsafePath)
    {
        using var folder = new PortableFolder();
        string archive = folder.CreateArchive((unsafePath, "unsafe"));

        await Assert.ThrowsAsync<InvalidDataException>(() => folder.PrepareAsync(archive));

        Assert.Empty(Directory.EnumerateDirectories(folder.Work));
        Assert.True(File.Exists(Path.Combine(folder.Install, "TokenUsage.App.exe")));
        Assert.False(File.Exists(Path.Combine(folder.Root, "escape.txt")));
    }

    [Fact]
    public async Task RejectsCaseAliasedZipEntries()
    {
        using var folder = new PortableFolder();
        string archive = folder.CreateArchive(("same.txt", "one"), ("SAME.txt", "two"));

        await Assert.ThrowsAsync<InvalidDataException>(() => folder.PrepareAsync(archive));
    }

    [Fact]
    public async Task LockedCliFileRollsBackEarlierWritesAndCanRetryAfterUnlock()
    {
        using var folder = new PortableFolder();
        string changed = Path.Combine(folder.Install, "a-change.txt");
        string locked = Path.Combine(folder.Install, "z-locked.txt");
        await File.WriteAllTextAsync(changed, "old");
        await File.WriteAllTextAsync(locked, "in use");
        folder.AddOwnedFile("a-change.txt", "z-locked.txt");
        string archive = folder.CreateArchive(("a-change.txt", "new"), ("z-locked.txt", "updated"));
        PortableUpdatePlan plan = await folder.PrepareAsync(archive);
        await SimulateExitedParentAsync(plan);

        using (var heldOpen = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(1, await PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId));
        }

        Assert.Equal("old", await File.ReadAllTextAsync(changed));
        Assert.Equal("in use", await File.ReadAllTextAsync(locked));
        Assert.Equal("failed", (await PortableUpdateInstaller.ReadResultAsync(plan.PlanPath))?.Status);
        Assert.Equal(0, await PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId));
        Assert.Equal("new", await File.ReadAllTextAsync(changed));
        Assert.Equal("updated", await File.ReadAllTextAsync(locked));
    }

    [Fact]
    public async Task DoesNotApplyWhileExactOwningProcessIsAlive()
    {
        using var folder = new PortableFolder();
        string target = Path.Combine(folder.Install, "a-change.txt");
        await File.WriteAllTextAsync(target, "old");
        folder.AddOwnedFile("a-change.txt");
        PortableUpdatePlan plan = await folder.PrepareAsync(folder.CreateArchive(("a-change.txt", "new")));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId, cancellation.Token));

        Assert.Equal("old", await File.ReadAllTextAsync(target));
        Assert.Null(await PortableUpdateInstaller.ReadResultAsync(plan.PlanPath));
    }

    [Fact]
    public async Task RefusesToOverwriteUnownedFileNameCollision()
    {
        using var folder = new PortableFolder();
        string personal = Path.Combine(folder.Install, "personal.txt");
        await File.WriteAllTextAsync(personal, "keep");

        await Assert.ThrowsAsync<InvalidDataException>(() => folder.PrepareAsync(folder.CreateArchive(("personal.txt", "release"))));

        Assert.Equal("keep", await File.ReadAllTextAsync(personal));
    }

    [Fact]
    public async Task RepeatedRecoveryPreservesForeignFileWhenPreparedMoveNeverApplied()
    {
        using var folder = new PortableFolder();
        PortableUpdatePlan plan = await folder.PrepareAsync(folder.CreateArchive(("new-component.txt", "release")));
        await SimulateExitedParentAsync(plan);
        string root = Path.GetDirectoryName(plan.PlanPath)!;
        string target = Path.Combine(folder.Install, "new-component.txt");
        await File.WriteAllTextAsync(target, "foreign file");
        await File.WriteAllTextAsync(target + "." + Path.GetFileName(root) + ".tmp", "release");
        await File.WriteAllTextAsync(Path.Combine(root, "journal.json"),
            """{"Completed":false,"Entries":[{"Path":"new-component.txt","Existed":false}]}""");

        Assert.Equal(1, await PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId));
        Assert.Equal(1, await PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId));

        Assert.Equal("foreign file", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task StatusWriteFailureNeverRollsBackACommittedInstallation()
    {
        using var folder = new PortableFolder();
        PortableUpdatePlan plan = await folder.PrepareAsync(folder.CreateArchive(("new-component.txt", "release")));
        await SimulateExitedParentAsync(plan);
        Assert.Equal(0, await PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId));
        string resultPath = Path.Combine(Path.GetDirectoryName(plan.PlanPath)!, "result.json");
        File.Delete(resultPath);
        Directory.CreateDirectory(resultPath);

        Assert.Equal(1, await PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId));

        Assert.Equal("release", await File.ReadAllTextAsync(Path.Combine(folder.Install, "new-component.txt")));
        Assert.Equal("installed", (await PortableUpdateInstaller.ReadResultAsync(plan.PlanPath))?.Status);
    }

    [Fact]
    public async Task RearmBindsRetryToCurrentAppAndRefusesConcurrentWorker()
    {
        using var folder = new PortableFolder();
        PortableUpdatePlan plan = await folder.PrepareAsync(folder.CreateArchive(("a-change.txt", "new")));
        await SimulateExitedParentAsync(plan);
        string root = Path.GetDirectoryName(plan.PlanPath)!;
        using (var workerLock = new FileStream(Path.Combine(root, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => PortableUpdateInstaller.RearmAsync(plan.PlanPath, folder.Install));
        }

        PortableUpdatePlan rearmed = await PortableUpdateInstaller.RearmAsync(plan.PlanPath, folder.Install);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PortableUpdateInstaller.RunWorkerAsync(rearmed.PlanPath, rearmed.ParentProcessId, cancellation.Token));
        Assert.False(File.Exists(Path.Combine(folder.Install, "a-change.txt")));
    }

    [Fact]
    public async Task RejectsStagedFilesChangedAfterPreparation()
    {
        using var folder = new PortableFolder();
        PortableUpdatePlan plan = await folder.PrepareAsync(folder.CreateArchive(("a-change.txt", "new")));
        await SimulateExitedParentAsync(plan);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(plan.PlanPath)!, "stage", "a-change.txt"), "tampered");

        Assert.Equal(1, await PortableUpdateInstaller.RunWorkerAsync(plan.PlanPath, plan.ParentProcessId));
        Assert.False(File.Exists(Path.Combine(folder.Install, "a-change.txt")));
    }

    private static async Task SimulateExitedParentAsync(PortableUpdatePlan plan)
    {
        // A reused PID must not make the worker wait for an unrelated process.
        JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(plan.PlanPath))!;
        document["ParentStartTimeUtcTicks"] = 1;
        await File.WriteAllTextAsync(plan.PlanPath, document.ToJsonString());
    }

    private sealed class PortableFolder : IDisposable
    {
        public PortableFolder()
        {
            Root = Path.Combine(Path.GetTempPath(), "tokenusage-portable-update-tests", Guid.NewGuid().ToString("N"));
            Install = Path.Combine(Root, "installed");
            Work = Path.Combine(Install, "Data", "Updates");
            Directory.CreateDirectory(Path.Combine(Install, "cli"));
            File.WriteAllText(Path.Combine(Install, "TokenUsage.portable"), "TokenUsage portable distribution");
            foreach (string file in ProductFiles)
            {
                File.Copy(typeof(PortableUpdateInstallerTests).Assembly.Location, Path.Combine(Install, file));
            }
            AddOwnedFile();
        }

        public string Root { get; }
        public string Install { get; }
        public string Work { get; }
        private static readonly string[] ProductFiles = ["TokenUsage.App.exe", "TokenUsage.App.dll", "cli/tokenusage.exe", "cli/tokenusage.dll"];
        private static readonly Version ProductVersion = typeof(PortableUpdateInstallerTests).Assembly.GetName().Version!;

        public void AddOwnedFile(params string[] paths)
        {
            string inventory = Path.Combine(Install, "TokenUsage.files.json");
            string[] current = File.Exists(inventory)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(inventory))!
                : [.. ProductFiles, "TokenUsage.portable", "TokenUsage.files.json"];
            File.WriteAllText(inventory, JsonSerializer.Serialize(current.Concat(paths).Distinct(StringComparer.Ordinal).ToArray()));
        }

        public string CreateArchive(params (string Path, string Content)[] extras)
        {
            string archivePath = Path.Combine(Root, "release.zip");
            string architecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            string prefix = $"TokenUsage-{ProductVersion.ToString(3)}-win-{architecture}-portable/";
            using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(Path.Combine(Install, "TokenUsage.portable"), prefix + "TokenUsage.portable");
            foreach (string file in ProductFiles)
            {
                archive.CreateEntryFromFile(Path.Combine(Install, file), prefix + file);
            }

            foreach ((string path, string content) in extras)
            {
                using var writer = new StreamWriter(archive.CreateEntry(prefix + path).Open());
                writer.Write(content);
            }

            using (var writer = new StreamWriter(archive.CreateEntry(prefix + "TokenUsage.files.json").Open()))
            {
                writer.Write(JsonSerializer.Serialize(ReleaseMetadataFiles
                    .Concat(ProductFiles).Concat(extras.Select(extra => extra.Path)).ToArray()));
            }

            return archivePath;
        }

        public Task<PortableUpdatePlan> PrepareAsync(string archive) =>
            PortableUpdateInstaller.PrepareAsync(archive, ProductVersion, Install, Work);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
