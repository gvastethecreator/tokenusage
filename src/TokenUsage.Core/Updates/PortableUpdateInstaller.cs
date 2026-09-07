using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TokenUsage.Core.Updates;

/// <summary>Stages a verified portable release and applies it after its owning app exits.</summary>
public static class PortableUpdateInstaller
{
    public const string WorkerArgument = "--tokenusage-update-worker";
    private const string MarkerName = "TokenUsage.portable";
    private const string InventoryName = "TokenUsage.files.json";
    private const int MaximumFiles = 12_000;
    private const long MaximumExpandedBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumFileBytes = 1024L * 1024 * 1024;
    private const int MaximumDocumentBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<PortableUpdatePlan> PrepareAsync(
        string archivePath,
        Version expectedVersion,
        string installDirectory,
        string workDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedVersion);
        installDirectory = Path.GetFullPath(installDirectory);
        workDirectory = Path.GetFullPath(workDirectory);
        ValidateInstallation(installDirectory);
        string[] previousFiles = await ReadInventoryAsync(installDirectory, cancellationToken).ConfigureAwait(false);
        EnsureNoReparsePoints(archivePath);
        EnsureNoReparsePoints(workDirectory);
        if ((IsWithin(installDirectory, workDirectory)
                || string.Equals(Path.TrimEndingDirectorySeparator(installDirectory), Path.TrimEndingDirectorySeparator(workDirectory), StringComparison.OrdinalIgnoreCase))
            && !IsWithin(Path.Combine(installDirectory, "Data"), workDirectory))
        {
            throw new InvalidDataException("Update staging inside the installation must be under Data.");
        }

        Directory.CreateDirectory(workDirectory);
        string transaction = Path.Combine(workDirectory, "update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);
        string stage = Path.Combine(transaction, "stage");
        string worker = Path.Combine(transaction, "worker");
        string planPath = Path.Combine(transaction, "plan.json");
        try
        {
            IReadOnlyList<PayloadFile> files = await ExtractAsync(
                archivePath, stage, expectedVersion, cancellationToken).ConfigureAwait(false);
            ValidateInstallation(stage, expectedVersion);
            string[] nextFiles = await ReadInventoryAsync(stage, cancellationToken).ConfigureAwait(false);
            if (!nextFiles.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(files.Select(file => file.Path)))
            {
                throw new InvalidDataException("The portable release inventory does not match its payload.");
            }

            var ownedFiles = previousFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (PayloadFile file in files)
            {
                if (File.Exists(ResolvePayloadPath(installDirectory, file.Path)) && !ownedFiles.Contains(file.Path))
                {
                    throw new InvalidDataException("The release would overwrite a file that does not belong to TokenUsage.");
                }
            }
            await CopyWorkerAsync(installDirectory, worker, previousFiles, cancellationToken)
                .ConfigureAwait(false);
            using Process parent = Process.GetCurrentProcess();
            var document = new PlanDocument(
                1,
                Path.GetFileName(transaction),
                installDirectory,
                NormalizeVersion(expectedVersion).ToString(),
                parent.Id,
                parent.StartTime.ToUniversalTime().Ticks,
                false,
                previousFiles,
                files.ToArray());
            await WriteDocumentAsync(planPath, document, cancellationToken).ConfigureAwait(false);
            return new(planPath, Path.Combine(worker, "tokenusage.exe"), parent.Id);
        }
        catch
        {
            DeleteOwnedTransaction(transaction);
            throw;
        }
    }

    public static async Task<PortableUpdatePlan> RearmAsync(
        string planPath, string installDirectory, CancellationToken cancellationToken = default)
    {
        string root = ValidatePlanPath(planPath);
        using var transactionLock = new FileStream(
            Path.Combine(root, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        PlanDocument plan = await ReadPlanAsync(planPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(Path.GetFullPath(installDirectory), Path.GetFullPath(plan.InstallDirectory), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The pending update belongs to a different portable installation.");
        }

        PortableUpdateResult? result = await ReadResultAsync(planPath, cancellationToken).ConfigureAwait(false);
        if (result?.Status == "installed") { throw new InvalidOperationException("The pending update is already installed."); }
        ValidateInstallation(Path.Combine(root, "stage"), Version.Parse(plan.Version));
        await VerifyPayloadAsync(root, plan, cancellationToken).ConfigureAwait(false);
        using Process parent = Process.GetCurrentProcess();
        await WriteDocumentAsync(planPath, plan with
        {
            ParentProcessId = parent.Id,
            ParentStartTimeUtcTicks = parent.StartTime.ToUniversalTime().Ticks,
            Relaunch = false,
        }, cancellationToken).ConfigureAwait(false);
        string resultPath = Path.Combine(root, "result.json");
        EnsureNoReparsePoints(resultPath);
        if (File.Exists(resultPath)) { File.Delete(resultPath); }
        return new(planPath, Path.Combine(root, "worker", "tokenusage.exe"), parent.Id);
    }

    public static async Task SetRelaunchAsync(
        string planPath, bool relaunch, CancellationToken cancellationToken = default)
    {
        PlanDocument plan = await ReadPlanAsync(planPath, cancellationToken).ConfigureAwait(false);
        await WriteDocumentAsync(planPath, plan with { Relaunch = relaunch }, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<PortableUpdateResult?> ReadResultAsync(
        string planPath, CancellationToken cancellationToken = default)
    {
        string root = ValidatePlanPath(planPath);
        string resultPath = Path.Combine(root, "result.json");
        string journalPath = Path.Combine(root, "journal.json");
        if (File.Exists(journalPath)
            && (await ReadDocumentAsync<InstallJournal>(journalPath, cancellationToken).ConfigureAwait(false)).Completed)
        {
            PlanDocument plan = await ReadPlanAsync(planPath, cancellationToken).ConfigureAwait(false);
            ValidateInstallation(plan.InstallDirectory, Version.Parse(plan.Version));
            return new("installed", "The portable update was installed.", File.GetLastWriteTimeUtc(journalPath));
        }

        return File.Exists(resultPath)
            ? await ReadDocumentAsync<PortableUpdateResult>(resultPath, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public static async Task CleanupAfterSuccessfulInstallAsync(
        string planPath, CancellationToken cancellationToken = default)
    {
        string root = ValidatePlanPath(planPath);
        _ = await ReadPlanAsync(planPath, cancellationToken).ConfigureAwait(false);
        using var transactionLock = new FileStream(
            Path.Combine(root, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if ((await ReadResultAsync(planPath, cancellationToken).ConfigureAwait(false))?.Status != "installed")
        {
            throw new InvalidOperationException("Only a successfully installed update can discard its staging files.");
        }

        foreach (string directory in new[] { "stage", "worker" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            string ownedPath = Path.Combine(root, directory);
            if (Directory.Exists(ownedPath)) { DeleteCheckedTree(ownedPath); }
        }
        // Keep plan, journal, result and backup so the previous files remain recoverable.
    }

    public static async Task<int> RunWorkerAsync(
        string planPath, int parentProcessId, CancellationToken cancellationToken = default)
    {
        string root = ValidatePlanPath(planPath);
        PlanDocument plan = await ReadPlanAsync(planPath, cancellationToken).ConfigureAwait(false);
        if (parentProcessId <= 0 || parentProcessId != plan.ParentProcessId)
        {
            throw new InvalidDataException("The update worker does not belong to this application process.");
        }

        // One worker owns a plan. FileShare.None is released even if the worker crashes.
        using var transactionLock = new FileStream(
            Path.Combine(root, "worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await WaitForParentAsync(plan, cancellationToken).ConfigureAwait(false);
        plan = await ReadPlanAsync(planPath, cancellationToken).ConfigureAwait(false);
        if (plan.ParentProcessId != parentProcessId)
        {
            throw new InvalidDataException("The update plan changed process ownership.");
        }

        string installLockDirectory = Path.Combine(plan.InstallDirectory, "Data", "Updates");
        EnsureNoReparsePoints(installLockDirectory);
        Directory.CreateDirectory(installLockDirectory);
        string installLockPath = Path.Combine(installLockDirectory, "install.lock");
        EnsureNoReparsePoints(installLockPath);
        using var installLock = new FileStream(installLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string journalPath = Path.Combine(root, "journal.json");
        var journal = new InstallJournal(false, []);
        bool committed = false;
        try
        {
            if (File.Exists(journalPath))
            {
                try
                {
                    InstallJournal recovered = await ReadDocumentAsync<InstallJournal>(journalPath, cancellationToken)
                        .ConfigureAwait(false);
                    var allowed = plan.Files.Select(file => file.Path).Concat(plan.PreviousFiles).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (recovered.Entries is null || recovered.Entries.Count > MaximumFiles * 2
                        || recovered.Entries.Any(entry => entry is null || !allowed.Contains(entry.Path))
                        || recovered.Entries.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != recovered.Entries.Count)
                    {
                        throw new InvalidDataException("The update journal is invalid.");
                    }

                    journal = recovered;
                }
                catch (InvalidDataException)
                {
                    await WriteResultAsync(root, "rollback-required", "The update journal needs recovery. The previous files remain in the update backup.")
                        .ConfigureAwait(false);
                    return 1;
                }
                if (journal.Completed)
                {
                    committed = true;
                    ValidateInstallation(plan.InstallDirectory, Version.Parse(plan.Version));
                    await WriteResultAsync(root, "installed", "The portable update was installed.").ConfigureAwait(false);
                    return 0;
                }

                await RollBackAsync(root, plan, journal).ConfigureAwait(false);
                journal = new(false, []);
                await WriteDocumentAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RollBackAsync(root, plan, journal).ConfigureAwait(false);
            }

            ValidateInstallation(plan.InstallDirectory);
            ValidateInstallation(Path.Combine(root, "stage"), Version.Parse(plan.Version));
            await VerifyPayloadAsync(root, plan, cancellationToken).ConfigureAwait(false);
            var ownedFiles = plan.PreviousFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Check all collisions before the first write, including files added after preparation.
            foreach (PayloadFile payload in plan.Files)
            {
                string target = ResolvePayloadPath(plan.InstallDirectory, payload.Path);
                EnsureNoReparsePoints(target);
                if (File.Exists(target) && !ownedFiles.Contains(payload.Path))
                {
                    throw new InvalidDataException("The release would overwrite a file that does not belong to TokenUsage.");
                }
            }

            foreach (PayloadFile payload in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = ResolvePayloadPath(plan.InstallDirectory, payload.Path);
                string backup = ResolvePayloadPath(Path.Combine(root, "backup"), payload.Path);
                EnsureNoReparsePoints(target);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                bool existed = File.Exists(target);
                if (existed && !ownedFiles.Contains(payload.Path))
                {
                    throw new InvalidDataException("The release would overwrite a file that does not belong to TokenUsage.");
                }
                if (existed && new FileInfo(target).Length == payload.Length
                    && string.Equals(await HashFileAsync(target, cancellationToken).ConfigureAwait(false), payload.Sha256, StringComparison.Ordinal))
                {
                    // Keep identical dependencies in place, including their timestamps and locks.
                    continue;
                }
                if (existed)
                {
                    EnsureNoReparsePoints(backup);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, overwrite: true);
                    FlushFile(backup);
                }

                string temporary = target + "." + plan.TransactionId + ".tmp";
                EnsureNoReparsePoints(temporary);
                File.Copy(ResolvePayloadPath(Path.Combine(root, "stage"), payload.Path), temporary, overwrite: false);
                FlushFile(temporary);
                journal.Entries.Add(new(payload.Path, existed));
                await WriteDocumentAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
                await MovePreparedWithRetryAsync(temporary, target, existed, cancellationToken).ConfigureAwait(false);
            }

            var nextFiles = plan.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string obsolete in plan.PreviousFiles.Where(file => !nextFiles.Contains(file)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string target = ResolvePayloadPath(plan.InstallDirectory, obsolete);
                EnsureNoReparsePoints(target);
                if (!File.Exists(target)) { continue; }
                string backup = ResolvePayloadPath(Path.Combine(root, "backup"), obsolete);
                EnsureNoReparsePoints(backup);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup, overwrite: true);
                FlushFile(backup);
                journal.Entries.Add(new(obsolete, true));
                await WriteDocumentAsync(journalPath, journal, cancellationToken).ConfigureAwait(false);
                File.Delete(target);
            }

            ValidateInstallation(plan.InstallDirectory, Version.Parse(plan.Version));
            journal = journal with { Completed = true };
            await WriteDocumentAsync(journalPath, journal, CancellationToken.None).ConfigureAwait(false);
            committed = true;
            await WriteResultAsync(root, "installed", "The portable update was installed.").ConfigureAwait(false);
            if (plan.Relaunch)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(plan.InstallDirectory, "TokenUsage.App.exe"))
                    {
                        UseShellExecute = false,
                        WorkingDirectory = plan.InstallDirectory,
                    })?.Dispose();
                }
                catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
                {
                    await WriteResultAsync(root, "installed", "The update was installed. Open TokenUsage to continue.")
                        .ConfigureAwait(false);
                }
            }

            return 0;
        }
        catch (Exception exception) when (committed && (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException))
        {
            // The durable journal is the commit point. Status/relaunch failures must not undo it.
            return 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            try
            {
                await RollBackAsync(root, plan, journal).ConfigureAwait(false);
                await WriteDocumentAsync(journalPath, new InstallJournal(false, []), CancellationToken.None)
                    .ConfigureAwait(false);
                await WriteResultAsync(root, "failed", "The update could not be installed. The previous files were restored.")
                    .ConfigureAwait(false);
            }
            catch (Exception rollbackException) when (rollbackException is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                await WriteResultAsync(root, "rollback-required", "Close TokenUsage and its CLI, then retry to restore the previous files.")
                    .ConfigureAwait(false);
            }

            return 1;
        }
    }

    private static async Task<IReadOnlyList<PayloadFile>> ExtractAsync(
        string archivePath, string stage, Version expectedVersion, CancellationToken cancellationToken)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is 0 or > MaximumFiles)
        {
            throw new InvalidDataException("The portable archive has an invalid entry count.");
        }

        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("Portable updates require x64 or ARM64."),
        };
        string expectedRoot = $"TokenUsage-{NormalizeVersion(expectedVersion).ToString(3)}-win-{architecture}-portable";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var payload = new List<PayloadFile>();
        long expanded = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.FullName;
            bool directory = name.EndsWith('/');
            string[] segments = name.TrimEnd('/').Split('/');
            if (segments.Length == 0 || segments[0] != expectedRoot
                || segments.Any(IsUnsafeSegment) || name.Contains('\\', StringComparison.Ordinal)
                || !seen.Add(name.TrimEnd('/'))
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || ((entry.ExternalAttributes >> 16) & 0xF000) is 0xA000 or 0x6000 or 0x2000)
            {
                throw new InvalidDataException("The portable archive contains an unsafe path or link.");
            }

            if (segments.Length == 1)
            {
                if (!directory) { throw new InvalidDataException("The portable archive must have one root directory."); }
                continue;
            }

            string relative = string.Join('/', segments.Skip(1));
            ValidatePayloadPath(relative);
            string destination = ResolvePayloadPath(stage, relative);
            if (directory)
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            if (entry.Length < 0 || entry.Length > MaximumFileBytes || expanded > MaximumExpandedBytes - entry.Length)
            {
                throw new InvalidDataException("The portable archive exceeds the extraction limit.");
            }
            expanded += entry.Length;

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using Stream input = entry.Open();
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    written += read;
                    if (written > entry.Length) { throw new InvalidDataException("The archive entry exceeds its declared size."); }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                if (written != entry.Length) { throw new InvalidDataException("The archive entry is incomplete."); }
                output.Flush(flushToDisk: true);
            }

            payload.Add(new(relative, entry.Length, await HashFileAsync(destination, cancellationToken).ConfigureAwait(false)));
        }

        return payload.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task CopyWorkerAsync(
        string installDirectory, string destination, string[] ownedFiles, CancellationToken cancellationToken)
    {
        string source = Path.Combine(installDirectory, "cli");
        EnsureNoReparsePoints(source);
        if (!File.Exists(Path.Combine(source, "tokenusage.exe")))
        {
            throw new InvalidDataException("The installed portable CLI is missing.");
        }

        int count = 0;
        long bytes = 0;
        foreach (string relative in ownedFiles.Where(file => file.StartsWith("cli/", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = ResolvePayloadPath(installDirectory, relative);
            EnsureNoReparsePoints(file);
            bytes = checked(bytes + new FileInfo(file).Length);
            if (++count > MaximumFiles || bytes > MaximumExpandedBytes)
            {
                throw new InvalidDataException("The installed CLI exceeds the staging limit.");
            }

            string target = ResolvePayloadPath(destination, relative[4..]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = File.OpenRead(file);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task VerifyPayloadAsync(string root, PlanDocument plan, CancellationToken cancellationToken)
    {
        foreach (PayloadFile file in plan.Files)
        {
            string path = ResolvePayloadPath(Path.Combine(root, "stage"), file.Path);
            EnsureNoReparsePoints(path);
            if (new FileInfo(path).Length != file.Length
                || !string.Equals(await HashFileAsync(path, cancellationToken).ConfigureAwait(false), file.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The staged portable files changed after verification.");
            }
        }
    }

    private static async Task WaitForParentAsync(PlanDocument plan, CancellationToken cancellationToken)
    {
        Process parent;
        try { parent = Process.GetProcessById(plan.ParentProcessId); }
        catch (ArgumentException) { return; }
        using (parent)
        {
            try
            {
                if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTimeUtcTicks) { return; }
                await parent.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException) { /* The original process exited between inspection and waiting. */ }
        }
    }

    private static async Task ReplaceWithRetryAsync(
        string source, string target, string transactionId, bool overwrite, CancellationToken cancellationToken)
    {
        string temporary = target + "." + transactionId + ".tmp";
        EnsureNoReparsePoints(temporary);
        try
        {
            File.Copy(source, temporary, overwrite: false);
            FlushFile(temporary);
            await MovePreparedWithRetryAsync(temporary, target, overwrite, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    private static async Task MovePreparedWithRetryAsync(
        string temporary, string target, bool overwrite, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoints(target);
            try
            {
                File.Move(temporary, target, overwrite);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task RollBackAsync(string root, PlanDocument plan, InstallJournal journal)
    {
        for (int index = journal.Entries.Count - 1; index >= 0; index--)
        {
            JournalEntry entry = journal.Entries[index];
            string target = ResolvePayloadPath(plan.InstallDirectory, entry.Path);
            EnsureNoReparsePoints(target);
            string temporary = target + "." + plan.TransactionId + ".tmp";
            EnsureNoReparsePoints(temporary);
            if (File.Exists(temporary))
            {
                // The replacement was prepared before its journal entry. An unconsumed
                // temp proves Move did not install it; preserve any foreign destination.
                journal.Entries.RemoveAt(index);
                await WriteDocumentAsync(Path.Combine(root, "journal.json"), journal, CancellationToken.None).ConfigureAwait(false);
                File.Delete(temporary);
                continue;
            }
            if (entry.Existed)
            {
                string backup = ResolvePayloadPath(Path.Combine(root, "backup"), entry.Path);
                EnsureNoReparsePoints(backup);
                if (!File.Exists(backup)) { throw new InvalidDataException("An update rollback backup is missing."); }
                // An unchanged locked file needs no replacement (a common CLI-in-use failure).
                if (File.Exists(target)
                    && string.Equals(await HashFileAsync(target, CancellationToken.None).ConfigureAwait(false),
                        await HashFileAsync(backup, CancellationToken.None).ConfigureAwait(false), StringComparison.Ordinal))
                {
                    journal.Entries.RemoveAt(index);
                    await WriteDocumentAsync(Path.Combine(root, "journal.json"), journal, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }

                string rollbackId = plan.TransactionId + "-rollback";
                string rollbackTemporary = target + "." + rollbackId + ".tmp";
                EnsureNoReparsePoints(rollbackTemporary);
                if (File.Exists(rollbackTemporary)) { File.Delete(rollbackTemporary); }
                await ReplaceWithRetryAsync(backup, target, rollbackId, true, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else if (File.Exists(target))
            {
                File.Delete(target);
            }

            journal.Entries.RemoveAt(index);
            await WriteDocumentAsync(Path.Combine(root, "journal.json"), journal, CancellationToken.None).ConfigureAwait(false);
        }

        // A crash may occur after preparing a temp but before adding its journal entry.
        foreach (PayloadFile file in plan.Files)
        {
            string temporary = ResolvePayloadPath(plan.InstallDirectory, file.Path) + "." + plan.TransactionId + ".tmp";
            EnsureNoReparsePoints(temporary);
            if (File.Exists(temporary)) { File.Delete(temporary); }
        }
    }

    private static void ValidateInstallation(string root, Version? expectedVersion = null)
    {
        ValidateTargetRoot(root);
        foreach (string name in new[] { "TokenUsage.App.exe", "TokenUsage.App.dll", "cli/tokenusage.exe", "cli/tokenusage.dll" })
        {
            string path = ResolvePayloadPath(root, name);
            EnsureNoReparsePoints(path);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                throw new InvalidDataException("The portable application or CLI is incomplete.");
            }

            FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
            if (!string.Equals(version.ProductName, "TokenUsage", StringComparison.Ordinal)
                || (expectedVersion is not null
                    && new Version(version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart)
                        != NormalizeVersion(expectedVersion)))
            {
                throw new InvalidDataException("The portable application identity or version does not match this release.");
            }
        }
    }

    private static void ValidateTargetRoot(string root)
    {
        root = Path.GetFullPath(root);
        EnsureNoReparsePoints(root);
        if (string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)!), StringComparison.OrdinalIgnoreCase)
            || Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git")))
        {
            throw new InvalidDataException("A repository or drive root cannot be updated as a portable installation.");
        }

        string marker = Path.Combine(root, MarkerName);
        EnsureNoReparsePoints(marker);
        if (!File.Exists(marker) || new FileInfo(marker).Length is <= 0 or > 4096
            || !File.ReadAllText(marker).Contains("TokenUsage portable distribution", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The target is not a TokenUsage portable installation.");
        }

    }

    private static async Task<PlanDocument> ReadPlanAsync(string planPath, CancellationToken cancellationToken)
    {
        string root = ValidatePlanPath(planPath);
        PlanDocument plan = await ReadDocumentAsync<PlanDocument>(planPath, cancellationToken).ConfigureAwait(false);
        if (plan.SchemaVersion != 1 || plan.TransactionId != Path.GetFileName(root)
            || plan.ParentProcessId <= 0 || plan.ParentStartTimeUtcTicks <= 0 || !Version.TryParse(plan.Version, out _)
            || plan.Files is null || plan.Files.Length is 0 or > MaximumFiles
            || plan.PreviousFiles is null || plan.PreviousFiles.Length is 0 or > MaximumFiles
            || plan.Files.Any(file => file is null)
            || plan.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Files.Length)
        {
            throw new InvalidDataException("The update plan is invalid.");
        }

        if (!Path.IsPathFullyQualified(plan.InstallDirectory)) { throw new InvalidDataException("The installation path must be absolute."); }
        ValidateTargetRoot(plan.InstallDirectory);
        ValidateInventory(plan.PreviousFiles);
        long total = 0;
        foreach (PayloadFile file in plan.Files)
        {
            ValidatePayloadPath(file.Path);
            if (file.Length < 0 || file.Length > MaximumFileBytes || total > MaximumExpandedBytes - file.Length
                || file.Sha256 is null || file.Sha256.Length != 64 || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("The update payload is invalid.");
            }
            total += file.Length;
        }

        return plan;
    }

    private static async Task<string[]> ReadInventoryAsync(string root, CancellationToken cancellationToken)
    {
        string[] files = await ReadDocumentAsync<string[]>(Path.Combine(root, InventoryName), cancellationToken)
            .ConfigureAwait(false);
        ValidateInventory(files);
        return files;
    }

    private static void ValidateInventory(string[] files)
    {
        if (files.Length is 0 or > MaximumFiles
            || files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length
            || !files.Contains(InventoryName, StringComparer.Ordinal)
            || !files.Contains(MarkerName, StringComparer.Ordinal))
        {
            throw new InvalidDataException("The portable file inventory is invalid.");
        }

        foreach (string file in files) { ValidatePayloadPath(file); }
    }

    private static string ValidatePlanPath(string planPath)
    {
        planPath = Path.GetFullPath(planPath);
        string root = Path.GetDirectoryName(planPath)!;
        string name = Path.GetFileName(root);
        if (Path.GetFileName(planPath) != "plan.json" || !name.StartsWith("update-", StringComparison.Ordinal)
            || !Guid.TryParseExact(name[7..], "N", out _))
        {
            throw new InvalidDataException("The update plan is not in an owned transaction directory.");
        }

        EnsureNoReparsePoints(planPath);
        return root;
    }

    private static void ValidatePayloadPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) { throw new InvalidDataException("The update path is empty."); }
        string[] segments = path.Split('/');
        if (segments.Any(IsUnsafeSegment) || path.Contains('\\', StringComparison.Ordinal)
            || string.Equals(segments[0], "Data", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[0], ".git", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("An update cannot replace user data or use an unsafe path.");
        }
    }

    private static bool IsUnsafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".."
            || segment.EndsWith('.') || segment.EndsWith(' ')
            || segment.IndexOfAny([':', '<', '>', '"', '|', '?', '*', '\\']) >= 0
            || segment.Any(char.IsControl)) { return true; }
        string name = segment.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL"
            || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))
                && name[3] is >= '0' and <= '9');
    }

    private static string ResolvePayloadPath(string root, string relative)
    {
        ValidatePayloadPath(relative);
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(root, path)) { throw new InvalidDataException("The update path escapes its owning directory."); }
        return path;
    }

    private static bool IsWithin(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void EnsureNoReparsePoints(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { attributes = 0; }
            catch (DirectoryNotFoundException) { attributes = 0; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Update paths must not contain symbolic links or junctions.");
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<T> ReadDocumentAsync<T>(string path, CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaximumDocumentBytes) { throw new InvalidDataException("The update document exceeds its size limit."); }
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("The update document is empty.");
        }
        catch (JsonException exception) { throw new InvalidDataException("The update document is invalid.", exception); }
    }

    private static async Task WriteDocumentAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(path);
        string temporary = path + ".new";
        EnsureNoReparsePoints(temporary);
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static Task WriteResultAsync(string root, string status, string message) => WriteDocumentAsync(
        Path.Combine(root, "result.json"), new PortableUpdateResult(status, message, DateTimeOffset.UtcNow), CancellationToken.None);

    private static void FlushFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.Flush(flushToDisk: true);
    }

    private static Version NormalizeVersion(Version version) => new(
        version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));

    private static void DeleteOwnedTransaction(string root)
    {
        _ = ValidatePlanPath(Path.Combine(root, "plan.json"));
        // Only this not-yet-published GUID directory is removed. Installed backups are retained.
        DeleteCheckedTree(root);
    }

    private static void DeleteCheckedTree(string root)
    {
        EnsureNoReparsePoints(root);
        var directories = new Queue<string>();
        directories.Enqueue(root);
        while (directories.TryDequeue(out string? directory))
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                EnsureNoReparsePoints(entry);
                if (Directory.Exists(entry)) { directories.Enqueue(entry); }
            }
        }

        Directory.Delete(root, recursive: true);
    }

    private sealed record PayloadFile(string Path, long Length, string Sha256);
    private sealed record PlanDocument(int SchemaVersion, string TransactionId, string InstallDirectory,
        string Version, int ParentProcessId, long ParentStartTimeUtcTicks, bool Relaunch, string[] PreviousFiles, PayloadFile[] Files);
    private sealed record JournalEntry(string Path, bool Existed);
    private sealed record InstallJournal(bool Completed, List<JournalEntry> Entries);
}
