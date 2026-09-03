using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;
using TokenUsage.App.Controls;

namespace TokenUsage.App.Services;

internal static class ShareCaptureService
{
    private const string DestinationToken = "TokenUsage.ShareCaptureDestination";
    private const int MaximumRenderDimension = 8192;
    private const int CapturePadding = 10;
    private static readonly SemaphoreSlim CaptureGate = new(1, 1);

    public static async Task<string> GetDestinationPathAsync()
    {
        StorageFolder folder = await ResolveDestinationFolderAsync();
        return folder.Path;
    }

    public static async Task<string?> PickDestinationAsync(nint ownerWindow)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, ownerWindow);
        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return null;
        }

        StorageApplicationPermissions.FutureAccessList.AddOrReplace(
            DestinationToken,
            folder);
        return folder.Path;
    }

    public static async Task<string> ResetDestinationAsync()
    {
        if (StorageApplicationPermissions.FutureAccessList.ContainsItem(DestinationToken))
        {
            StorageApplicationPermissions.FutureAccessList.Remove(DestinationToken);
        }

        return (await ResolveDownloadsFolderAsync()).Path;
    }

    public static Task<ShareCaptureResult> CaptureAsync(
        FrameworkElement captureRoot,
        string captureKind,
        Windows.UI.Color backgroundColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureRoot);
        return CaptureAsync(
            [captureRoot],
            captureKind,
            backgroundColor,
            cancellationToken);
    }

    public static async Task<ShareCaptureResult> CaptureAsync(
        IReadOnlyList<FrameworkElement> captureRoots,
        string captureKind,
        Windows.UI.Color backgroundColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureRoots);
        if (captureRoots.Count == 0 || captureRoots.Any(captureRoot => captureRoot is null))
        {
            throw new ArgumentException("At least one capture surface is required.", nameof(captureRoots));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(captureKind);

        await CaptureGate.WaitAsync(cancellationToken);
        try
        {
            foreach (FrameworkElement captureRoot in captureRoots)
            {
                DismissTransientOverlays(captureRoot);
                captureRoot.UpdateLayout();
                if (captureRoot.ActualWidth <= 0 || captureRoot.ActualHeight <= 0)
                {
                    throw new InvalidOperationException("The capture surface is not ready.");
                }
            }

            double scale = CalculateScale(
                captureRoots.Max(captureRoot => captureRoot.ActualWidth),
                captureRoots.Sum(captureRoot => captureRoot.ActualHeight));

            var renderedSurfaces = new List<RenderedCaptureSurface>(captureRoots.Count);
            foreach (FrameworkElement captureRoot in captureRoots)
            {
                renderedSurfaces.Add(await RenderSurfaceAsync(
                    captureRoot,
                    scale,
                    backgroundColor));
            }

            return await SaveCaptureAsync(
                renderedSurfaces,
                captureKind,
                backgroundColor,
                cancellationToken);
        }
        finally
        {
            CaptureGate.Release();
        }
    }

    public static async Task<ShareCaptureResult> CaptureScrollableAsync(
        FrameworkElement headerRoot,
        ScrollViewer scrollViewer,
        FrameworkElement contentRoot,
        string captureKind,
        Windows.UI.Color backgroundColor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(headerRoot);
        ArgumentNullException.ThrowIfNull(scrollViewer);
        ArgumentNullException.ThrowIfNull(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(captureKind);

        await CaptureGate.WaitAsync(cancellationToken);
        ScrollBarVisibility originalScrollBarVisibility =
            scrollViewer.VerticalScrollBarVisibility;
        double originalOffset = scrollViewer.VerticalOffset;
        try
        {
            DismissTransientOverlays(headerRoot);
            DismissTransientOverlays(contentRoot);
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            headerRoot.UpdateLayout();
            contentRoot.UpdateLayout();
            scrollViewer.UpdateLayout();
            await Task.Yield();

            if (headerRoot.ActualWidth <= 0
                || headerRoot.ActualHeight <= 0
                || scrollViewer.ActualWidth <= 0
                || scrollViewer.ActualHeight <= 0)
            {
                throw new InvalidOperationException("The capture surface is not ready.");
            }

            double contentHeight = Math.Max(
                contentRoot.ActualHeight,
                scrollViewer.ExtentHeight);
            if (!double.IsFinite(contentHeight) || contentHeight <= 0)
            {
                throw new InvalidOperationException("The scrollable capture has no content.");
            }

            double scale = CalculateScale(
                Math.Max(headerRoot.ActualWidth, scrollViewer.ActualWidth),
                Math.Max(headerRoot.ActualHeight, scrollViewer.ActualHeight));
            var renderedSurfaces = new List<RenderedCaptureSurface>
            {
                await RenderSurfaceAsync(headerRoot, scale, backgroundColor),
            };
            await ScrollToAsync(scrollViewer, 0, cancellationToken);
            RenderedCaptureSurface? pendingViewport = await RenderSurfaceAsync(
                scrollViewer,
                scale,
                backgroundColor);
            double pixelsPerDip = pendingViewport.Height / scrollViewer.ActualHeight;
            if (!double.IsFinite(pixelsPerDip) || pixelsPerDip <= 0)
            {
                throw new InvalidOperationException("The scrollable capture scale is invalid.");
            }

            int contentPixelHeight = Math.Max(
                1,
                (int)Math.Round(contentHeight * pixelsPerDip));
            int capturedPixelHeight = 0;
            while (capturedPixelHeight < contentPixelHeight)
            {
                double requestedOffset = Math.Min(
                    capturedPixelHeight / pixelsPerDip,
                    scrollViewer.ScrollableHeight);
                await ScrollToAsync(scrollViewer, requestedOffset, cancellationToken);
                RenderedCaptureSurface viewport;
                if (pendingViewport is not null && requestedOffset <= 0.75)
                {
                    viewport = pendingViewport;
                    pendingViewport = null;
                }
                else
                {
                    viewport = await RenderSurfaceAsync(
                        scrollViewer,
                        scale,
                        backgroundColor);
                }

                int viewportStart = Math.Min(
                    capturedPixelHeight,
                    Math.Max(
                        0,
                        (int)Math.Round(scrollViewer.VerticalOffset * pixelsPerDip)));
                int cropTop = Math.Clamp(
                    capturedPixelHeight - viewportStart,
                    0,
                    Math.Max(0, viewport.Height - 1));
                int captureHeight = Math.Min(
                    viewport.Height - cropTop,
                    contentPixelHeight - capturedPixelHeight);
                if (captureHeight <= 0)
                {
                    throw new InvalidOperationException(
                        "The scrollable capture could not advance through the content.");
                }

                renderedSurfaces.Add(CropVertical(viewport, cropTop, captureHeight));
                capturedPixelHeight += captureHeight;
            }

            return await SaveCaptureAsync(
                renderedSurfaces,
                captureKind,
                backgroundColor,
                cancellationToken);
        }
        finally
        {
            scrollViewer.VerticalScrollBarVisibility = originalScrollBarVisibility;
            scrollViewer.ChangeView(null, originalOffset, null, disableAnimation: true);
            scrollViewer.UpdateLayout();
            CaptureGate.Release();
        }
    }

    private static double CalculateScale(double width, double height) => Math.Min(
        1,
        (MaximumRenderDimension - (CapturePadding * 2d)) / Math.Max(width, height));

    private static async Task<RenderedCaptureSurface> RenderSurfaceAsync(
        FrameworkElement captureRoot,
        double scale,
        Windows.UI.Color backgroundColor)
    {
        int renderWidth = Math.Max(1, (int)Math.Round(captureRoot.ActualWidth * scale));
        int renderHeight = Math.Max(1, (int)Math.Round(captureRoot.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap();
        if (scale >= 0.999)
        {
            await bitmap.RenderAsync(captureRoot);
        }
        else
        {
            await bitmap.RenderAsync(captureRoot, renderWidth, renderHeight);
        }
        IBuffer pixelBuffer = await bitmap.GetPixelsAsync();
        byte[] surfacePixels = new byte[pixelBuffer.Length];
        using (DataReader reader = DataReader.FromBuffer(pixelBuffer))
        {
            reader.ReadBytes(surfacePixels);
        }

        FlattenTransparency(surfacePixels, backgroundColor);
        return new RenderedCaptureSurface(
            surfacePixels,
            bitmap.PixelWidth,
            bitmap.PixelHeight);
    }

    private static async Task ScrollToAsync(
        ScrollViewer scrollViewer,
        double offset,
        CancellationToken cancellationToken)
    {
        double target = Math.Clamp(offset, 0, scrollViewer.ScrollableHeight);
        if (Math.Abs(scrollViewer.VerticalOffset - target) <= 0.75)
        {
            scrollViewer.UpdateLayout();
            return;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
        {
            if (!args.IsIntermediate
                && Math.Abs(scrollViewer.VerticalOffset - target) <= 0.75)
            {
                completion.TrySetResult(true);
            }
        }

        scrollViewer.ViewChanged += OnViewChanged;
        try
        {
            bool accepted = scrollViewer.ChangeView(
                null,
                target,
                null,
                disableAnimation: true);
            scrollViewer.UpdateLayout();
            if (Math.Abs(scrollViewer.VerticalOffset - target) <= 0.75)
            {
                return;
            }

            if (!accepted)
            {
                throw new InvalidOperationException("The report could not be scrolled for capture.");
            }

            Task timeout = Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            Task finished = await Task.WhenAny(completion.Task, timeout);
            cancellationToken.ThrowIfCancellationRequested();
            scrollViewer.UpdateLayout();
            if (finished != completion.Task
                || Math.Abs(scrollViewer.VerticalOffset - target) > 0.75)
            {
                throw new InvalidOperationException("The report did not finish scrolling for capture.");
            }
        }
        finally
        {
            scrollViewer.ViewChanged -= OnViewChanged;
        }
    }

    private static RenderedCaptureSurface CropVertical(
        RenderedCaptureSurface source,
        int top,
        int height)
    {
        if (top == 0 && height == source.Height)
        {
            return source;
        }

        byte[] pixels = new byte[checked(source.Width * height * 4)];
        System.Buffer.BlockCopy(
            source.Pixels,
            checked(top * source.Width * 4),
            pixels,
            0,
            pixels.Length);
        return new RenderedCaptureSurface(pixels, source.Width, height);
    }

    private static async Task<ShareCaptureResult> SaveCaptureAsync(
        IReadOnlyList<RenderedCaptureSurface> renderedSurfaces,
        string captureKind,
        Windows.UI.Color backgroundColor,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int renderedWidth = renderedSurfaces.Max(surface => surface.Width);
        int renderedHeight = renderedSurfaces.Sum(surface => surface.Height);
        byte[] combinedPixels = StackVertically(
            renderedSurfaces,
            renderedWidth,
            renderedHeight,
            backgroundColor);
        byte[] paddedPixels = AddPadding(
            combinedPixels,
            renderedWidth,
            renderedHeight,
            backgroundColor);
        int outputWidth = renderedWidth + (CapturePadding * 2);
        int outputHeight = renderedHeight + (CapturePadding * 2);

        StorageFolder destination = await ResolveDestinationFolderAsync();
        string timestamp = DateTimeOffset.Now.ToString(
            "yyyy-MM-dd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture);
        StorageFile file = await destination.CreateFileAsync(
            $"TokenUsage-{captureKind}-{timestamp}.png",
            CreationCollisionOption.GenerateUniqueName);
        using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
        {
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
                BitmapEncoder.PngEncoderId,
                stream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)outputWidth,
                (uint)outputHeight,
                96,
                96,
                paddedPixels);
            await encoder.FlushAsync();
        }

        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy,
        };
        package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        Clipboard.SetContent(package);
        Clipboard.Flush();
        return new ShareCaptureResult(file.Path, outputWidth, outputHeight);
    }

    private static byte[] StackVertically(
        IReadOnlyList<RenderedCaptureSurface> surfaces,
        int width,
        int height,
        Windows.UI.Color background)
    {
        byte[] output = new byte[checked(width * height * 4)];
        FillBackground(output, background);
        int outputStride = width * 4;
        int top = 0;
        foreach (RenderedCaptureSurface surface in surfaces)
        {
            int sourceStride = surface.Width * 4;
            int leftOffset = Math.Max(0, (width - surface.Width) / 2) * 4;
            for (int row = 0; row < surface.Height; row++)
            {
                System.Buffer.BlockCopy(
                    surface.Pixels,
                    row * sourceStride,
                    output,
                    ((top + row) * outputStride) + leftOffset,
                    sourceStride);
            }

            top += surface.Height;
        }

        return output;
    }

    private static void FlattenTransparency(byte[] pixels, Windows.UI.Color background)
    {
        for (int index = 0; index <= pixels.Length - 4; index += 4)
        {
            byte alpha = pixels[index + 3];
            if (alpha == byte.MaxValue)
            {
                continue;
            }

            int inverseAlpha = byte.MaxValue - alpha;
            pixels[index] = Composite(pixels[index], background.B, inverseAlpha);
            pixels[index + 1] = Composite(pixels[index + 1], background.G, inverseAlpha);
            pixels[index + 2] = Composite(pixels[index + 2], background.R, inverseAlpha);
            pixels[index + 3] = byte.MaxValue;
        }
    }

    private static void DismissTransientOverlays(DependencyObject root)
    {
        if (root is UsageTrendChart chart)
        {
            chart.DismissHover();
        }

        int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DismissTransientOverlays(
                Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, index));
        }
    }

    private static byte[] AddPadding(
        byte[] pixels,
        int width,
        int height,
        Windows.UI.Color background)
    {
        int outputWidth = width + (CapturePadding * 2);
        int outputHeight = height + (CapturePadding * 2);
        byte[] output = new byte[checked(outputWidth * outputHeight * 4)];
        FillBackground(output, background);

        int sourceStride = width * 4;
        int outputStride = outputWidth * 4;
        int leftOffset = CapturePadding * 4;
        for (int row = 0; row < height; row++)
        {
            System.Buffer.BlockCopy(
                pixels,
                row * sourceStride,
                output,
                ((row + CapturePadding) * outputStride) + leftOffset,
                sourceStride);
        }

        return output;
    }

    private static void FillBackground(byte[] output, Windows.UI.Color background)
    {
        for (int index = 0; index < output.Length; index += 4)
        {
            output[index] = background.B;
            output[index + 1] = background.G;
            output[index + 2] = background.R;
            output[index + 3] = byte.MaxValue;
        }
    }

    private static byte Composite(byte foreground, byte background, int inverseAlpha) =>
        (byte)Math.Clamp(foreground + ((background * inverseAlpha + 127) / 255), 0, 255);

    private static async Task<StorageFolder> ResolveDestinationFolderAsync()
    {
        if (StorageApplicationPermissions.FutureAccessList.ContainsItem(DestinationToken))
        {
            try
            {
                return await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(
                    DestinationToken);
            }
            catch (Exception exception) when (exception is FileNotFoundException
                or UnauthorizedAccessException
                or System.Runtime.InteropServices.COMException)
            {
                StorageApplicationPermissions.FutureAccessList.Remove(DestinationToken);
            }
        }

        return await ResolveDownloadsFolderAsync();
    }

    private static async Task<StorageFolder> ResolveDownloadsFolderAsync()
    {
        string downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        Directory.CreateDirectory(downloadsPath);
        return await StorageFolder.GetFolderFromPathAsync(downloadsPath);
    }
}

internal sealed record RenderedCaptureSurface(byte[] Pixels, int Width, int Height);

internal sealed record ShareCaptureResult(string FilePath, int PixelWidth, int PixelHeight);
