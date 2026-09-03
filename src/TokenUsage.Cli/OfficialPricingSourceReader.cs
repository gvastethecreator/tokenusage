using System.Net;
using System.Text;
using TokenUsage.Providers.Pricing;

namespace TokenUsage.Cli;

internal delegate Task<PricingRefreshSourceInput> PricingRefreshSourceReader(
    PricingRefreshSourceDefinition definition,
    CancellationToken cancellationToken);

internal sealed class OfficialPricingSourceReader : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private readonly HttpClient _client;

    public OfficialPricingSourceReader()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _client = new HttpClient(handler)
        {
            Timeout = RequestTimeout,
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("TokenUsage-Pricing-Audit/1.0");
    }

    public async Task<PricingRefreshSourceInput> ReadAsync(
        PricingRefreshSourceDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        if (!PricingRefreshManifest.Sources.Any(allowed =>
                allowed.Source.Id == definition.Source.Id
                && allowed.Source.OfficialUri == definition.Source.OfficialUri))
        {
            throw new InvalidOperationException("The pricing source is not allowlisted.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, definition.Source.OfficialUri);
            using HttpResponseMessage response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Failed(definition, PricingRefreshReadStatus.Unavailable);
            }

            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null
                || !(mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)
                     || mediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase)
                     || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)))
            {
                return Failed(definition, PricingRefreshReadStatus.UnsupportedContentType);
            }

            if (response.Content.Headers.ContentLength is long length
                && length > definition.MaximumBytes)
            {
                return Failed(definition, PricingRefreshReadStatus.Oversized);
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            byte[] bytes = await ReadBoundedAsync(
                    stream,
                    definition.MaximumBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length > definition.MaximumBytes)
            {
                return Failed(definition, PricingRefreshReadStatus.Oversized);
            }

            string content = new UTF8Encoding(false, true).GetString(bytes);
            return new(definition.Source.Id, PricingRefreshReadStatus.Available, content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed(definition, PricingRefreshReadStatus.Unavailable);
        }
    }

    public void Dispose() => _client.Dispose();

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        byte[] block = new byte[8192];
        while (buffer.Length <= maximumBytes)
        {
            int remaining = maximumBytes + 1 - checked((int)buffer.Length);
            int read = await stream.ReadAsync(
                    block.AsMemory(0, Math.Min(block.Length, remaining)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }

        return buffer.ToArray();
    }

    private static PricingRefreshSourceInput Failed(
        PricingRefreshSourceDefinition definition,
        PricingRefreshReadStatus status) =>
        new(definition.Source.Id, status, null);
}
