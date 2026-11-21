using BeckerBearings.App.Services;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace BeckerBearings.App.Platforms.Windows;

public sealed class WindowsCameraService : ICameraService
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private Exception? _frameError;
    private int _arrived;
    private int _withoutBitmap;
    private int _wrongSize;
    private readonly System.Threading.Channels.Channel<CameraFrame> _frames =
        System.Threading.Channels.Channel.CreateBounded<CameraFrame>(new System.Threading.Channels.BoundedChannelOptions(1)
        { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });

    public async Task<IReadOnlyList<VideoDevice>> ListAsync() =>
        (await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture))
        .Select(x => new VideoDevice(x.Id, x.Name)).ToArray();

    public async Task OpenAsync(string id)
    {
        await DisposeAsync();
        while (_frames.Reader.TryRead(out _)) { }
        _frameError = null;
        _arrived = _withoutBitmap = _wrongSize = 0;
        _capture = new MediaCapture();
        try
        {
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            });
            var source = _capture.FrameSources.Values.FirstOrDefault(x =>
                x.Info.SourceKind == MediaFrameSourceKind.Color &&
                x.SupportedFormats.Any(f => f.VideoFormat.Width == 1280 && f.VideoFormat.Height == 480))
                ?? throw new InvalidOperationException("Camera has no 1280×480 color stream for this calibration.");
            var format = source.SupportedFormats.First(x =>
                x.VideoFormat.Width == 1280 && x.VideoFormat.Height == 480);
            await source.SetFormatAsync(format);
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _reader.FrameArrived += OnFrame;
            var status = await _reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
                throw new InvalidOperationException($"Camera stream did not start: {status}.");
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        Interlocked.Increment(ref _arrived);
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            using var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null) { Interlocked.Increment(ref _withoutBitmap); return; }
            if (bitmap.PixelWidth != 1280 || bitmap.PixelHeight != 480)
            { Interlocked.Increment(ref _wrongSize); return; }
            using var bgra = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore);
            var pixels = new byte[1280 * 480 * 4];
            bgra.CopyToBuffer(pixels.AsBuffer());
            var bgr = new byte[1280 * 480 * 3];
            for (int i = 0, j = 0; j < bgr.Length; i += 4, j += 3)
            { bgr[j] = pixels[i]; bgr[j + 1] = pixels[i + 1]; bgr[j + 2] = pixels[i + 2]; }
            _frames.Writer.TryWrite(new CameraFrame(bgr, 1280, 480,
                System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency));
        }
        catch (Exception ex) { _frameError = ex; }
    }

    public async Task<CameraFrame> CaptureAsync(CancellationToken cancellationToken)
    {
        try { return await _frames.Reader.ReadAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No camera frame arrived. Reader callbacks: {_arrived}; " +
                $"missing bitmap: {_withoutBitmap}; wrong size: {_wrongSize}. " +
                (_frameError is null ? "" : $"Last frame error: {_frameError.Message}"));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_reader is not null)
        {
            var reader = _reader; _reader = null;
            reader.FrameArrived -= OnFrame;
            try { await reader.StopAsync(); }
            finally { reader.Dispose(); }
        }
        _capture?.Dispose(); _capture = null;
    }
}
