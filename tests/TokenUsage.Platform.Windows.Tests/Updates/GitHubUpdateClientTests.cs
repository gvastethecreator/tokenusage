using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using TokenUsage.Core.Updates;
using TokenUsage.Runtime.Windows.Updates;

namespace TokenUsage.Platform.Windows.Tests.Updates;

public sealed class GitHubUpdateClientTests : IDisposable
{
    private const string AssetName = "TokenUsage-1.2.3-win-x64-portable.zip";
    private const string AssetUrl = "https://github.com/gvastethecreator/tokenusage/releases/download/v1.2.3/" + AssetName;
    private static readonly byte[] PackageBytes = [1, 2, 3, 4];
    private static readonly string PackageHash = Convert.ToHexString(SHA256.HashData(PackageBytes)).ToLowerInvariant();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TokenUsage.UpdateFeed.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("x64", UpdatePackageKind.Portable, "-portable.zip")]
    [InlineData("ARM64", UpdatePackageKind.Portable, "-portable.zip")]
    [InlineData("x64", UpdatePackageKind.Msix, ".msix")]
    [InlineData("arm64", UpdatePackageKind.Msix, ".msixbundle")]
    public async Task FindsTheExactNewerPackageForThisInstallation(string architecture, UpdatePackageKind kind, string suffix)
    {
        string name = $"TokenUsage-1.2.3-win-{architecture.ToLowerInvariant()}{suffix}";
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal("https://api.github.com/repos/gvastethecreator/tokenusage/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            return Metadata(assetName: name);
        }));

        UpdateRelease? result = await new GitHubUpdateClient(http).FindUpdateAsync(new Version(1, 2, 2, 0), architecture, kind);

        Assert.NotNull(result);
        Assert.Equal(new Version(1, 2, 3), result.Version);
        Assert.Equal(name, result.AssetName);
        Assert.Equal(PackageHash, result.Sha256);
        Assert.Equal(kind, result.Kind);
    }

    [Theory]
    [InlineData("v1.2.3", false, false)]
    [InlineData("v1.2.2", false, false)]
    [InlineData("v1.2.4", true, false)]
    [InlineData("v1.2.4", false, true)]
    [InlineData("v1.2.4-beta.1", false, false)]
    [InlineData("v1.2.4.0", false, false)]
    public async Task IgnoresSameOlderDraftPrereleaseAndNonCanonicalVersions(string tag, bool draft, bool prerelease)
    {
        using var http = new HttpClient(new Handler(_ => Metadata(tag, draft, prerelease)));

        UpdateRelease? result = await new GitHubUpdateClient(http).FindUpdateAsync(new Version(1, 2, 3, 0), "x64", UpdatePackageKind.Portable);

        Assert.Null(result);
    }

    [Fact]
    public async Task MissingStableReleaseIsNotAnError()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await new GitHubUpdateClient(http).FindUpdateAsync(new Version(1, 0, 0), "x64", UpdatePackageKind.Portable));
    }

    [Fact]
    public async Task UnsignedOrWrongArchitecturePackagesAreNeverSelected()
    {
        using var http = new HttpClient(new Handler(_ => Metadata(assetName: "TokenUsage-1.2.3-win-x64-unsigned.msix")));
        Assert.Null(await new GitHubUpdateClient(http).FindUpdateAsync(new Version(1, 0, 0), "x64", UpdatePackageKind.Msix));
        Assert.Null(await new GitHubUpdateClient(http).FindUpdateAsync(new Version(1, 0, 0), "arm64", UpdatePackageKind.Msix));
    }

    [Theory]
    [InlineData(null, 4, null)]
    [InlineData("sha256:abc", 4, null)]
    [InlineData("valid", 0, null)]
    [InlineData("valid", 1073741825, null)]
    [InlineData("valid", 4, "https://github.com/another/repository/releases/download/v1.2.3/TokenUsage-1.2.3-win-x64-portable.zip")]
    [InlineData("valid", 4, "https://github.com.evil.example/package.zip")]
    public async Task RejectsUnverifiedOrOutOfBoundsReleaseAssets(string? digest, long size, string? url)
    {
        using var http = new HttpClient(new Handler(_ => Metadata(digest: digest == "valid" ? "sha256:" + PackageHash : digest, size: size, url: url)));

        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdateClient(http).FindUpdateAsync(new Version(1, 0, 0), "x64", UpdatePackageKind.Portable));
    }

    [Fact]
    public async Task MetadataIsBoundedEvenWithoutContentLength()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamingContent(new byte[2 * 1024 * 1024 + 1]),
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdateClient(http).FindUpdateAsync(new Version(1, 0, 0), "x64", UpdatePackageKind.Portable));
    }

    [Fact]
    public async Task DownloadFollowsTrustedRedirectAndOnlyReturnsVerifiedCompletedFile()
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            if (requests.Count == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                redirect.Headers.Location = new Uri("https://release-assets.githubusercontent.com/github-production-release-asset/asset?signature=example");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PackageBytes) };
        }));

        string path = await new GitHubUpdateClient(http).DownloadAsync(Release(), _directory);

        Assert.Equal(PackageBytes, await File.ReadAllBytesAsync(path));
        Assert.EndsWith(AssetName, path, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(_directory));
        Assert.Equal(2, requests.Count);
    }

    [Theory]
    [InlineData("http://github.com/gvastethecreator/tokenusage/releases/download/v1.2.3/package.zip")]
    [InlineData("https://release-assets.githubusercontent.com.evil.example/package.zip")]
    [InlineData("https://github.com/another/repository/releases/download/v1.2.3/package.zip")]
    [InlineData("https://user:secret@release-assets.githubusercontent.com/package.zip")]
    public async Task UntrustedRedirectIsNotRequested(string destination)
    {
        int requests = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
            redirect.Headers.Location = new Uri(destination);
            return redirect;
        }));

        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdateClient(http).DownloadAsync(Release(), _directory));

        Assert.Equal(1, requests);
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(4, false)]
    public async Task ShortOversizedAndCorruptDownloadsLeaveNoInstallableFile(long declaredSize, bool correctHash)
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamingContent(PackageBytes),
        }));
        UpdateRelease release = Release() with { Size = declaredSize, Sha256 = correctHash ? PackageHash : new string('0', 64) };

        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdateClient(http).DownloadAsync(release, _directory));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CancellationRemovesOnlyItsOwnPartialDownload()
    {
        Directory.CreateDirectory(_directory);
        string existingFile = Path.Combine(_directory, "keep.txt");
        await File.WriteAllTextAsync(existingFile, "keep");
        using var cancellation = new CancellationTokenSource();
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PackageBytes) }));
        var progress = new InlineProgress(value =>
        {
            if (value > 0)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubUpdateClient(http).DownloadAsync(Release(), _directory, progress, cancellation.Token));

        Assert.Equal([existingFile], Directory.GetFiles(_directory));
        Assert.Equal("keep", await File.ReadAllTextAsync(existingFile));
    }

    private static UpdateRelease Release() =>
        new(new Version(1, 2, 3), "v1.2.3", AssetName, new Uri(AssetUrl), PackageBytes.Length, PackageHash, UpdatePackageKind.Portable);

    private static HttpResponseMessage Metadata(
        string tag = "v1.2.3",
        bool draft = false,
        bool prerelease = false,
        string assetName = AssetName,
        string? digest = "default",
        long size = 4,
        string? url = null) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                tag_name = tag,
                draft,
                prerelease,
                assets = new[]
                {
                    new
                    {
                        name = assetName,
                        state = "uploaded",
                        digest = digest == "default" ? "sha256:" + PackageHash : digest,
                        size,
                        browser_download_url = url ?? $"https://github.com/gvastethecreator/tokenusage/releases/download/{tag}/{assetName}",
                    },
                },
            }),
        };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class StreamingContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
