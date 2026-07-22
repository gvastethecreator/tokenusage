using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WOpenUsage.Providers.Codex;

public sealed class CodexAppServerClient : ICodexQuotaClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly CodexClientOptions _options;
    private readonly CodexJsonlTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private long _nextRequestId;
    private bool _handshakeCompleted;
    private bool _faulted;
    private int _disposeStarted;

    public CodexAppServerClient(
        Stream input,
        Stream output,
        CodexClientOptions options,
        TimeProvider? timeProvider = null,
        bool leaveOpen = false)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _transport = new CodexJsonlTransport(
            input,
            output,
            options.MaximumLineBytes,
            leaveOpen);
    }

    public async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfFaulted();
            if (_handshakeCompleted)
            {
                return;
            }

            long requestId = NextRequestId();
            var request = new RpcRequest<InitializeParams>(
                requestId,
                "initialize",
                new InitializeParams(
                    new ClientInfo(
                        _options.ClientName,
                        _options.ClientVersion,
                        _options.ClientTitle),
                    new InitializeCapabilities(ExperimentalApi: false)));

            using JsonDocument response = await ExchangeAsync(request, requestId, cancellationToken)
                .ConfigureAwait(false);
            RequireResultObject(response.RootElement);

            await WriteWithTimeoutAsync(new RpcNotification("initialized"), cancellationToken)
                .ConfigureAwait(false);
            _handshakeCompleted = true;
        }
        catch (Exception exception) when (
            exception is CodexProtocolException or OperationCanceledException)
        {
            _faulted = true;
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<CodexRateLimitsSnapshot> ReadRateLimitsAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfFaulted();
            if (!_handshakeCompleted)
            {
                throw new InvalidOperationException(
                    "Codex app-server handshake must complete before reading rate limits.");
            }

            long requestId = NextRequestId();
            var request = new RpcRequest<object?>(
                requestId,
                "account/rateLimits/read",
                Params: null);
            using JsonDocument response = await ExchangeAsync(request, requestId, cancellationToken)
                .ConfigureAwait(false);
            JsonElement result = RequireResultObject(response.RootElement);
            return CodexRateLimitsParser.Parse(result);
        }
        catch (Exception exception) when (
            exception is CodexProtocolException or OperationCanceledException)
        {
            _faulted = true;
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<CodexAccountStatus> ReadAccountStatusAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfFaulted();
            if (!_handshakeCompleted)
            {
                throw new InvalidOperationException(
                    "Codex app-server handshake must complete before reading account status.");
            }

            long requestId = NextRequestId();
            var request = new RpcRequest<AccountReadParams>(
                requestId,
                "account/read",
                new AccountReadParams(RefreshToken: false));
            using JsonDocument response = await ExchangeAsync(request, requestId, cancellationToken)
                .ConfigureAwait(false);
            JsonElement result = RequireResultObject(response.RootElement);
            return CodexAccountStatusParser.Parse(result);
        }
        catch (Exception exception) when (
            exception is CodexProtocolException or OperationCanceledException)
        {
            _faulted = true;
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async Task<CodexTokenUsageSnapshot> ReadTokenUsageAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            ThrowIfFaulted();
            if (!_handshakeCompleted)
            {
                throw new InvalidOperationException(
                    "Codex app-server handshake must complete before reading token usage.");
            }

            long requestId = NextRequestId();
            var request = new RpcRequest<object?>(
                requestId,
                "account/usage/read",
                Params: null);
            using JsonDocument response = await ExchangeAsync(request, requestId, cancellationToken)
                .ConfigureAwait(false);
            JsonElement result = RequireResultObject(response.RootElement);
            return CodexTokenUsageParser.Parse(result);
        }
        catch (Exception exception) when (
            exception is CodexProtocolException or OperationCanceledException)
        {
            _faulted = true;
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        await _requestGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _requestGate.Release();
            _lifetimeCancellation.Dispose();
        }
    }

    private async Task<JsonDocument> ExchangeAsync<TParams>(
        RpcRequest<TParams> request,
        long expectedRequestId,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.RequestTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token,
            _lifetimeCancellation.Token);

        try
        {
            await WriteAsync(request, linked.Token).ConfigureAwait(false);
            while (true)
            {
                byte[] line = await _transport.ReadLineAsync(linked.Token).ConfigureAwait(false);
                JsonDocument response = ParseMessage(line);
                JsonElement root = response.RootElement;

                if (IsNotification(root))
                {
                    response.Dispose();
                    continue;
                }

                try
                {
                    RequireMatchingRequestId(root, expectedRequestId);
                    ThrowIfRpcError(root);
                    return response;
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CodexRequestTimeoutException();
        }
    }

    private async ValueTask WriteAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken)
    {
        byte[] payload;
        try
        {
            payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        }
        catch (JsonException)
        {
            throw new CodexProtocolException("Codex app-server request could not be encoded.");
        }

        await _transport.WriteLineAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteWithTimeoutAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.RequestTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token,
            _lifetimeCancellation.Token);

        try
        {
            await WriteAsync(message, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new CodexRequestTimeoutException();
        }
    }

    private static JsonDocument ParseMessage(byte[] line)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(
                line,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw new CodexProtocolException("Codex app-server returned an invalid JSONL message.");
            }

            return document;
        }
        catch (JsonException)
        {
            throw new CodexProtocolException("Codex app-server returned invalid JSON.");
        }
    }

    private static bool IsNotification(JsonElement root) =>
        !root.TryGetProperty("id", out _)
        && root.TryGetProperty("method", out JsonElement method)
        && method.ValueKind == JsonValueKind.String;

    private static void RequireMatchingRequestId(JsonElement root, long expectedRequestId)
    {
        if (!root.TryGetProperty("id", out JsonElement id))
        {
            throw new CodexProtocolException("Codex app-server response omitted its request ID.");
        }

        bool matches = id.ValueKind switch
        {
            JsonValueKind.Number => id.TryGetInt64(out long numericId)
                && numericId == expectedRequestId,
            JsonValueKind.String => long.TryParse(
                    id.GetString(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long textId)
                && textId == expectedRequestId,
            _ => false,
        };

        if (!matches)
        {
            throw new CodexProtocolException("Codex app-server response used an unexpected request ID.");
        }
    }

    private static void ThrowIfRpcError(JsonElement root)
    {
        if (!root.TryGetProperty("error", out JsonElement error))
        {
            return;
        }

        long? code = error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("code", out JsonElement codeElement)
            && codeElement.TryGetInt64(out long parsedCode)
                ? parsedCode
                : null;
        throw new CodexRpcException(code);
    }

    private static JsonElement RequireResultObject(JsonElement root)
    {
        if (!root.TryGetProperty("result", out JsonElement result)
            || result.ValueKind != JsonValueKind.Object)
        {
            throw new CodexProtocolException("Codex app-server response omitted its result object.");
        }

        return result;
    }

    private long NextRequestId() => Interlocked.Increment(ref _nextRequestId);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

    private void ThrowIfFaulted()
    {
        if (_faulted)
        {
            throw new CodexProtocolException(
                "Codex app-server session cannot be reused after a protocol or transport failure.");
        }
    }

    private sealed record RpcRequest<TParams>(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("method")] string Method,
        [property: JsonPropertyName("params")] TParams Params);

    private sealed record RpcNotification(
        [property: JsonPropertyName("method")] string Method);

    private sealed record InitializeParams(
        [property: JsonPropertyName("clientInfo")] ClientInfo ClientInfo,
        [property: JsonPropertyName("capabilities")] InitializeCapabilities Capabilities);

    private sealed record ClientInfo(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("title")] string? Title);

    private sealed record InitializeCapabilities(
        [property: JsonPropertyName("experimentalApi")] bool ExperimentalApi);

    private sealed record AccountReadParams(
        [property: JsonPropertyName("refreshToken")] bool RefreshToken);
}
