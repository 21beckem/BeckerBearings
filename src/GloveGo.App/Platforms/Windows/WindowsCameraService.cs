using GloveGo.App.Services;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace GloveGo.App.Platforms.Windows;

public sealed class WindowsCameraService : ICameraService
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private readonly System.Threading.Channels.Channel<CameraFrame> _frames =
        System.Threading.Channels.Channel.CreateBounded<CameraFrame>(new System.Threading.Channels.BoundedChannelOptions(1)
        { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });

    public async Task<IReadOnlyList<VideoDevice>> ListAsync() =>
        (await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture))
        .Select(x => new VideoDevice(x.Id, x.Name)).ToArray();

    public async Task OpenAsync(string id)
    {
        await DisposeAsync();
        _capture = new MediaCapture();
        await _capture.InitializeAsync(new MediaCaptureInitializationSettings
        { VideoDeviceId = id, StreamingCaptureMode = StreamingCaptureMode.Video });
        var source = _capture.FrameSources.Values.FirstOrDefault(x => x.Info.SourceKind == MediaFrameSourceKind.Color)
            ?? throw new InvalidOperationException("Selected camera has no color stream.");
        var format = source.SupportedFormats.FirstOrDefault(x =>
            x.VideoFormat.Width == 1280 && x.VideoFormat.Height == 480)
            ?? throw new InvalidOperationException("Camera has no 1280×480 mode for this calibration.");
        await source.SetFormatAsync(format);
        _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
        _reader.FrameArrived += OnFrame;
        if (await _reader.StartAsync() != MediaFrameReaderStartStatus.Success)
            throw new InvalidOperationException("Camera stream did not start.");
    }

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        using var frame = sender.TryAcquireLatestFrame();
        using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null || bitmap.PixelWidth != 1280 || bitmap.PixelHeight != 480) return;
        using var bgra = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
        var pixels = new byte[1280 * 480 * 4];
        bgra.CopyToBuffer(pixels.AsBuffer());
        var bgr = new byte[1280 * 480 * 3];
        for (int i = 0, j = 0; j < bgr.Length; i += 4, j += 3)
        { bgr[j] = pixels[i]; bgr[j + 1] = pixels[i + 1]; bgr[j + 2] = pixels[i + 2]; }
        _frames.Writer.TryWrite(new CameraFrame(bgr, 1280, 480,
            System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency));
    }

    public async Task<CameraFrame> CaptureAsync(CancellationToken cancellationToken) =>
        await _frames.Reader.ReadAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_reader is not null) { _reader.FrameArrived -= OnFrame; await _reader.StopAsync(); _reader.Dispose(); _reader = null; }
        _capture?.Dispose(); _capture = null;
    }
}
