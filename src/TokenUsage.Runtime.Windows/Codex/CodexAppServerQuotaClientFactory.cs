using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using WOpenUsage.Platform.Windows.Processes;
using WOpenUsage.Providers.Codex;

namespace WOpenUsage.Runtime.Windows.Codex;

public sealed class CodexAppServerQuotaClientFactory : ICodexQuotaClientFactory
{
    private readonly CodexClientOptions _clientOptions;
    private readonly TimeProvider _clock;
    private readonly Channel<bool> _processSlot;

    public CodexAppServerQuotaClientFactory(
        TimeProvider clock,
        CodexClientOptions? clientOptions = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _clientOptions = clientOptions ?? new CodexClientOptions(
            "wopenusage",
            "0.1.0",
            "TokenUsage");
        _processSlot = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
        _processSlot.Writer.TryWrite(true);
    }

    public ValueTask<CodexClientAvailability> DetectAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CodexClientAvailability availability = CodexExecutableResolver.Resolve() switch
        {
            CodexExecutableResolution.Resolved => CodexClientAvailability.Available,
            CodexExecutableResolution.Missing => CodexClientAvailability.MissingCli,
            CodexExecutableResolution.InvalidOverride => CodexClientAvailability.Unavailable,
            _ => throw new InvalidOperationException("Unknown Codex executable resolution."),
        };

        return ValueTask.FromResult(availability);
    }

    public async Task<ICodexQuotaClient> CreateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _processSlot.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        bool processSlotTransferred = false;
        try
        {
            if (CodexExecutableResolver.Resolve()
                is not CodexExecutableResolution.Resolved executable)
            {
                throw new CodexClientUnavailableException();
            }

            CodexAppServerProcess process;
            try
            {
                process = await Task.Run(
                    () => CodexAppServerProcess.Start(executable),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (CodexAppServerProcessException)
            {
                throw new CodexClientUnavailableException();
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var client = new CodexAppServerClient(
                    process.ClientInput,
                    process.ClientOutput,
                    _clientOptions,
                    _clock,
                    leaveOpen: true);
                var owner = new ProcessOwnedCodexQuotaClient(
                    client,
                    process,
                    ReleaseProcessSlot);
                processSlotTransferred = true;
                return owner;
            }
            catch
            {
                await DisposeProcessAfterFailedCreationAsync(process).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            if (!processSlotTransferred)
            {
                ReleaseProcessSlot();
            }
        }
    }

    private void ReleaseProcessSlot()
    {
        if (!_processSlot.Writer.TryWrite(true))
        {
            throw new InvalidOperationException("The Codex process slot could not be released.");
        }
    }

    private static async Task DisposeProcessAfterFailedCreationAsync(
        CodexAppServerProcess process)
    {
        try
        {
            await process.DisposeAsync().ConfigureAwait(false);
        }
        catch (CodexAppServerProcessException)
        {
            // Cleanup ran through every supervised shutdown fallback.
        }
    }

    private sealed class ProcessOwnedCodexQuotaClient(
        CodexAppServerClient client,
        CodexAppServerProcess process,
        Action releaseProcessSlot) : ICodexQuotaClient
    {
        private int _disposeStarted;

        public Task HandshakeAsync(CancellationToken cancellationToken) =>
            client.HandshakeAsync(cancellationToken);

        public Task<CodexAccountStatus> ReadAccountStatusAsync(
            CancellationToken cancellationToken) =>
            client.ReadAccountStatusAsync(cancellationToken);

        public Task<CodexRateLimitsSnapshot> ReadRateLimitsAsync(
            CancellationToken cancellationToken) =>
            client.ReadRateLimitsAsync(cancellationToken);

        public Task<CodexTokenUsageSnapshot> ReadTokenUsageAsync(
            CancellationToken cancellationToken) =>
            client.ReadTokenUsageAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            {
                return;
            }

            try
            {
                ExceptionDispatchInfo? unexpectedFailure = null;
                try
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is IOException or ObjectDisposedException)
                {
                    // Cleanup ran; keep an already-read quota result usable.
                }
                catch (Exception exception)
                {
                    unexpectedFailure = ExceptionDispatchInfo.Capture(exception);
                }

                try
                {
                    await process.DisposeAsync().ConfigureAwait(false);
                }
                catch (CodexAppServerProcessException)
                {
                    // The process owner exhausted its shutdown path before throwing.
                }

                unexpectedFailure?.Throw();
            }
            finally
            {
                releaseProcessSlot();
            }
        }
    }
}
