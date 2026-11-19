using Android.Content;
using Android.Hardware.Camera2;
using Android.Media;
using Android.OS;
using Android.Views;
using Android.Runtime;
using Android.Graphics;
using GloveGo.App.Services;

namespace GloveGo.App.Platforms.Android;

public sealed class AndroidCameraService : ICameraService
{
    private readonly CameraManager _manager = (CameraManager)global::Android.App.Application.Context.GetSystemService(Context.CameraService)!;
    private readonly System.Threading.Channels.Channel<CameraFrame> _frames =
        System.Threading.Channels.Channel.CreateBounded<CameraFrame>(new System.Threading.Channels.BoundedChannelOptions(1)
        { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });
    private CameraDevice? _device;
    private CameraCaptureSession? _session;
    private ImageReader? _reader;
    private HandlerThread? _thread;
    private Handler? _handler;

    public Task<IReadOnlyList<VideoDevice>> ListAsync()
    {
        var list = _manager.GetCameraIdList().Select(id =>
        {
            var c = _manager.GetCameraCharacteristics(id);
            var external = (int?)c.Get(CameraCharacteristics.InfoSupportedHardwareLevel) ==
                (int)InfoSupportedHardwareLevel.External;
            return new VideoDevice(id, external ? $"External camera {id}" : $"Camera {id}");
        }).ToArray();
        return Task.FromResult<IReadOnlyList<VideoDevice>>(list);
    }

    public async Task OpenAsync(string id)
    {
        await DisposeAsync();
        if (global::Android.App.Application.Context.CheckSelfPermission(global::Android.Manifest.Permission.Camera) !=
            global::Android.Content.PM.Permission.Granted)
            throw new InvalidOperationException("Grant camera permission in Android settings, then retry.");
        var sizes = (global::Android.Util.Size[]?)_manager.GetCameraCharacteristics(id)
            .Get(CameraCharacteristics.ScalerStreamConfigurationMap)
            ?.JavaCast<global::Android.Hardware.Camera2.Params.StreamConfigurationMap>()
            .GetOutputSizes((int)ImageFormatType.Yuv420888);
        if (sizes?.Any(s => s.Width == 1280 && s.Height == 480) != true)
            throw new InvalidOperationException("External camera is not exposed at 1280×480 by Camera2.");
        _thread = new HandlerThread("GloveGoCamera"); _thread.Start();
        _handler = new Handler(_thread.Looper!);
        _reader = ImageReader.NewInstance(1280, 480, ImageFormatType.Yuv420888, 2);
        _reader.SetOnImageAvailableListener(new FrameListener(_frames.Writer), _handler);
        var opened = new TaskCompletionSource<CameraDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _manager.OpenCamera(id, new OpenCallback(opened), _handler);
        _device = await opened.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var ready = new TaskCompletionSource<CameraCaptureSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        _device.CreateCaptureSession(new List<Surface> { _reader.Surface! }, new SessionCallback(ready), _handler);
        _session = await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var request = _device.CreateCaptureRequest(CameraTemplate.Preview);
        request.AddTarget(_reader.Surface!);
        _session.SetRepeatingRequest(request.Build(), null, _handler);
    }

    public async Task<CameraFrame> CaptureAsync(CancellationToken cancellationToken) =>
        await _frames.Reader.ReadAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _session?.Close(); _session?.Dispose(); _session = null;
        _device?.Close(); _device?.Dispose(); _device = null;
        _reader?.Dispose(); _reader = null;
        _thread?.QuitSafely(); _thread = null;
        return ValueTask.CompletedTask;
    }

    private sealed class OpenCallback(TaskCompletionSource<CameraDevice> ready) : CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera) => ready.TrySetResult(camera);
        public override void OnDisconnected(CameraDevice camera) => ready.TrySetException(new IOException("Camera disconnected."));
        public override void OnError(CameraDevice camera, CameraError error) => ready.TrySetException(new IOException($"Camera error: {error}"));
    }

    private sealed class SessionCallback(TaskCompletionSource<CameraCaptureSession> ready) : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session) => ready.TrySetResult(session);
        public override void OnConfigureFailed(CameraCaptureSession session) => ready.TrySetException(new IOException("Camera format rejected."));
    }

    private sealed class FrameListener(System.Threading.Channels.ChannelWriter<CameraFrame> writer) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader reader)
        {
            using var image = reader.AcquireLatestImage();
            if (image is null) return;
            var planes = image.GetPlanes();
            var data = new byte[1280 * 480 * 3];
            var y = planes[0].Buffer!; var u = planes[1].Buffer!; var v = planes[2].Buffer!;
            for (var row = 0; row < 480; row++)
                for (var col = 0; col < 1280; col++)
                {
                    var yy = y.Get(row * planes[0].RowStride + col * planes[0].PixelStride) & 255;
                    var uvRow = row / 2; var uvCol = col / 2;
                    var uu = (u.Get(uvRow * planes[1].RowStride + uvCol * planes[1].PixelStride) & 255) - 128;
                    var vv = (v.Get(uvRow * planes[2].RowStride + uvCol * planes[2].PixelStride) & 255) - 128;
                    var pos = (row * 1280 + col) * 3;
                    data[pos] = (byte)Math.Clamp(yy + 1.772 * uu, 0, 255);
                    data[pos + 1] = (byte)Math.Clamp(yy - 0.344136 * uu - 0.714136 * vv, 0, 255);
                    data[pos + 2] = (byte)Math.Clamp(yy + 1.402 * vv, 0, 255);
                }
            writer.TryWrite(new CameraFrame(data, 1280, 480, image.Timestamp / 1e9));
        }
    }
}
