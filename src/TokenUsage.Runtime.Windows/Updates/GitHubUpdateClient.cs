using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using TokenUsage.Core.Updates;

namespace TokenUsage.Runtime.Windows.Updates;

/// <summary>
/// Reads stable releases and verifies their assets. The supplied client must disable
/// automatic redirects and cookies; update requests never use account credentials.
/// </summary>
public sealed class GitHubUpdateClient
{
    private const string Repository = "gvastethecreator/tokenusage";
    private const long MaxAssetBytes = 1024L * 1024 * 1024;
    private const int MaxMetadataBytes = 2 * 1024 * 1024;
    private const int MaxRedirects = 5;
    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{Repository}/releases/latest");
    private static readonly string[] MsixExtensions = [".msixbundle", ".msix", ".appxbundle", ".appx"];
    private readonly HttpClient _client;

    public GitHubUpdateClient(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        EnsureAnonymousClient();
    }

    public async Task<UpdateRelease?> FindUpdateAsync(
        Version installedVersion,
        string architecture,
        UpdatePackageKind kind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installedVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        architecture = architecture.ToLowerInvariant();
        if (architecture is not ("x64" or "arm64"))
        {
            throw new ArgumentException("The update architecture must be x64 or arm64.", nameof(architecture));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        using HttpResponseMessage response = await SendAsync(LatestReleaseUri, metadata: true, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        byte[] metadata = await ReadMetadataAsync(response.Content, cancellationToken).ConfigureAwait(false);
        using JsonDocument document = JsonDocument.Parse(metadata, new JsonDocumentOptions { MaxDepth = 32 });
        JsonElement release = document.RootElement;
        if (release.ValueKind != JsonValueKind.Object
            || !IsFalse(release, "draft")
            || !IsFalse(release, "prerelease")
            || !TryReadTag(GetString(release, "tag_name"), out Version version))
        {
            return null;
        }

        var installed = new Version(installedVersion.Major, installedVersion.Minor, Math.Max(0, installedVersion.Build));
        if (version <= installed)
        {
            return null;
        }

        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The update release has no valid asset list.");
        }

        string tag = "v" + version.ToString(3);
        foreach (string expectedName in AssetNames(version, architecture, kind))
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || GetString(asset, "name") != expectedName)
                {
                    continue;
                }

                string? digest = GetString(asset, "digest");
                if (GetString(asset, "state") != "uploaded"
                    || !asset.TryGetProperty("size", out JsonElement sizeElement)
                    || sizeElement.ValueKind != JsonValueKind.Number
                    || !sizeElement.TryGetInt64(out long size)
                    || size is <= 0 or > MaxAssetBytes
                    || digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                    || !IsSha256(digest[7..])
                    || !Uri.TryCreate(GetString(asset, "browser_download_url"), UriKind.Absolute, out Uri? downloadUri)
                    || !IsReleaseAssetUri(downloadUri, tag, expectedName))
                {
                    throw new InvalidDataException("The update asset is incomplete or has no trusted SHA-256 digest.");
                }

                return new UpdateRelease(version, tag, expectedName, downloadUri, size, digest[7..].ToLowerInvariant(), kind);
            }
        }

        return null;
    }

    public async Task<string> DownloadAsync(
        UpdateRelease release,
        string cacheDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ValidateRelease(release);
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.GetFullPath(cacheDirectory);
        Directory.CreateDirectory(directory);
        string completedPath = Path.Combine(directory, $"{Guid.NewGuid():N}-{release.AssetName}");
        string partialPath = completedPath + ".partial";

        try
        {
            using HttpResponseMessage response = await GetAssetResponseAsync(release.DownloadUri, cancellationToken).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
            {
                throw new InvalidDataException("The update download size does not match its release metadata.");
            }

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long downloaded = 0;
            byte[] buffer = new byte[128 * 1024];
            progress?.Report(0);
            long lastProgress = Stopwatch.GetTimestamp();
            await using (var output = new FileStream(partialPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            }))
            {
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    downloaded += count;
                    if (downloaded > release.Size)
                    {
                        throw new InvalidDataException("The update download exceeds its declared size.");
                    }

                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    if (downloaded == release.Size || Stopwatch.GetElapsedTime(lastProgress) >= TimeSpan.FromMilliseconds(100))
                    {
                        progress?.Report((double)downloaded / release.Size);
                        lastProgress = Stopwatch.GetTimestamp();
                    }
                }

                if (downloaded != release.Size
                    || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                {
                    throw new InvalidDataException("The update download failed its size or SHA-256 verification.");
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, completedPath);
            return completedPath;
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }
        }
    }

    private async Task<HttpResponseMessage> GetAssetResponseAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        Uri current = initialUri;
        for (int redirects = 0; ; redirects++)
        {
            HttpResponseMessage response = await SendAsync(current, metadata: false, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                Uri? location = response.Headers.Location;
                response.Dispose();
                if (redirects >= MaxRedirects || location is null)
                {
                    throw new InvalidDataException("The update download returned too many redirects or no destination.");
                }

                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                if (!IsAllowedDownloadUri(current))
                {
                    throw new InvalidDataException("The update download redirected outside the trusted GitHub hosts.");
                }

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                using (response)
                {
                    response.EnsureSuccessStatusCode();
                }
            }

            return response;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, bool metadata, CancellationToken cancellationToken)
    {
        EnsureAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TokenUsage-Updater", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(metadata ? "application/vnd.github+json" : "application/octet-stream"));
        if (metadata)
        {
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.RequestMessage?.RequestUri is Uri actualUri && actualUri != uri)
        {
            response.Dispose();
            throw new InvalidOperationException("Automatic HTTP redirects must be disabled for update requests.");
        }

        return response;
    }

    private void EnsureAnonymousClient()
    {
        if (_client.DefaultRequestHeaders.Authorization is not null
            || _client.DefaultRequestHeaders.Contains("Cookie")
            || _client.DefaultRequestHeaders.Contains("Proxy-Authorization"))
        {
            throw new InvalidOperationException("Update requests must use an anonymous HTTP client.");
        }
    }

    private static async Task<byte[]> ReadMetadataAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxMetadataBytes)
        {
            throw new InvalidDataException("The update release metadata is too large.");
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > MaxMetadataBytes)
            {
                throw new InvalidDataException("The update release metadata is too large.");
            }

            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }

    private static bool TryReadTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (tag is null || !tag.StartsWith('v'))
        {
            return false;
        }

        string[] parts = tag[1..].Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch)
            || major > ushort.MaxValue || minor > ushort.MaxValue || patch > ushort.MaxValue)
        {
            return false;
        }

        version = new Version(major, minor, patch);
        return tag == "v" + version.ToString(3);
    }

    private static IEnumerable<string> AssetNames(Version version, string architecture, UpdatePackageKind kind)
    {
        string prefix = $"TokenUsage-{version.ToString(3)}-win-{architecture}";
        return kind == UpdatePackageKind.Portable
            ? [prefix + "-portable.zip"]
            : MsixExtensions.Select(extension => prefix + extension);
    }

    private static void ValidateRelease(UpdateRelease release)
    {
        if (!TryReadTag(release.Tag, out Version version) || version != release.Version
            || !Enum.IsDefined(release.Kind)
            || !(AssetNames(version, "x64", release.Kind).Contains(release.AssetName, StringComparer.Ordinal)
                || AssetNames(version, "arm64", release.Kind).Contains(release.AssetName, StringComparer.Ordinal))
            || release.Size is <= 0 or > MaxAssetBytes
            || !IsSha256(release.Sha256)
            || !IsReleaseAssetUri(release.DownloadUri, release.Tag, release.AssetName))
        {
            throw new InvalidDataException("The update release is not a valid official package.");
        }
    }

    private static bool IsReleaseAssetUri(Uri uri, string tag, string assetName) =>
        IsSecureUri(uri)
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.Query.Length == 0
        && uri.AbsolutePath == $"/{Repository}/releases/download/{tag}/{assetName}";

    private static bool IsAllowedDownloadUri(Uri uri) =>
        IsSecureUri(uri) && (uri.Host.ToLowerInvariant() switch
        {
            "github.com" => uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal),
            "api.github.com" => uri.AbsolutePath.StartsWith($"/repos/{Repository}/releases/assets/", StringComparison.Ordinal),
            "release-assets.githubusercontent.com" or "objects.githubusercontent.com" => true,
            _ => false,
        });

    private static bool IsSecureUri(Uri uri) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

    private static bool IsFalse(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.False;

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
