using Android.Content;
using Android.Hardware.Camera2;
using Android.Hardware.Usb;
using Android.Media;
using Android.OS;
using Android.Views;
using Android.Runtime;
using Android.Graphics;
using BeckerBearings.App.Services;

namespace BeckerBearings.App.Platforms.Android;

public sealed class AndroidCameraService : ICameraService
{
    private readonly CameraManager _manager = (CameraManager)global::Android.App.Application.Context.GetSystemService(Context.CameraService)!;
    private readonly UsbManager _usb = (UsbManager)global::Android.App.Application.Context.GetSystemService(Context.UsbService)!;
    private readonly System.Threading.Channels.Channel<CameraFrame> _frames =
        System.Threading.Channels.Channel.CreateBounded<CameraFrame>(new System.Threading.Channels.BoundedChannelOptions(1)
        { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest });
    private CameraDevice? _device;
    private CameraCaptureSession? _session;
    private ImageReader? _reader;
    private HandlerThread? _thread;
    private Handler? _handler;
    private UsbDeviceConnection? _usbConnection;
    private JavaUvcCamera? _uvc;
    private UvcFrameListener? _uvcFrames;

    public Task<IReadOnlyList<VideoDevice>> ListAsync()
    {
        var list = _manager.GetCameraIdList().Select(id =>
        {
            var c = _manager.GetCameraCharacteristics(id);
            var external = (int?)c.Get(CameraCharacteristics.LensFacing) == (int)LensFacing.External;
            return new VideoDevice($"camera:{id}", external ? $"System external camera {id}" : $"Phone camera {id}");
        }).ToList();
        list.AddRange(_usb.DeviceList.Values.Where(IsUvc).Select(device =>
            new VideoDevice($"usb:{device.DeviceName}",
                $"USB UVC camera {device.VendorId:X4}:{device.ProductId:X4}" +
                (_usb.HasPermission(device) ? "" : " (permission required)"))));
        return Task.FromResult<IReadOnlyList<VideoDevice>>(list);
    }

    public async Task OpenAsync(string id)
    {
        await DisposeAsync();
        if (global::Android.App.Application.Context.CheckSelfPermission(global::Android.Manifest.Permission.Camera) !=
            global::Android.Content.PM.Permission.Granted)
            throw new InvalidOperationException("Grant camera permission in Android settings, then retry.");
        if (id.StartsWith("usb:", StringComparison.Ordinal))
        {
            await OpenUvcAsync(id[4..]);
            return;
        }
        if (!id.StartsWith("camera:", StringComparison.Ordinal))
            throw new InvalidOperationException("Unknown camera source.");
        var cameraId = id[7..];
        var sizes = (global::Android.Util.Size[]?)_manager.GetCameraCharacteristics(cameraId)
            .Get(CameraCharacteristics.ScalerStreamConfigurationMap)
            ?.JavaCast<global::Android.Hardware.Camera2.Params.StreamConfigurationMap>()
            .GetOutputSizes((int)ImageFormatType.Yuv420888);
        if (sizes?.Any(s => s.Width == 1280 && s.Height == 480) != true)
            throw new InvalidOperationException("External camera is not exposed at 1280×480 by Camera2.");
        _thread = new HandlerThread("BeckerBearingsCamera"); _thread.Start();
        _handler = new Handler(_thread.Looper!);
        _reader = ImageReader.NewInstance(1280, 480, ImageFormatType.Yuv420888, 2);
        _reader.SetOnImageAvailableListener(new FrameListener(_frames.Writer), _handler);
        var opened = new TaskCompletionSource<CameraDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _manager.OpenCamera(cameraId, new OpenCallback(opened), _handler);
        _device = await opened.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var ready = new TaskCompletionSource<CameraCaptureSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        _device.CreateCaptureSession(new List<Surface> { _reader.Surface! }, new SessionCallback(ready), _handler);
        _session = await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var request = _device.CreateCaptureRequest(CameraTemplate.Preview);
        request.AddTarget(_reader.Surface!);
        _session.SetRepeatingRequest(request.Build(), null, _handler);
    }

    private static bool IsUvc(UsbDevice device) =>
        device.DeviceClass == UsbClass.Video ||
        Enumerable.Range(0, device.InterfaceCount).Select(device.GetInterface)
            .Any(@interface => @interface?.InterfaceClass == UsbClass.Video);

    private async Task OpenUvcAsync(string deviceName)
    {
        var device = _usb.DeviceList.Values.FirstOrDefault(d => d.DeviceName == deviceName)
            ?? throw new InvalidOperationException("USB camera disconnected. Refresh devices and retry.");
        if (!IsUvc(device)) throw new InvalidOperationException("Selected USB device does not expose a UVC video interface.");
        if (!_usb.HasPermission(device) && !await UsbPermission.RequestAsync(_usb, device))
            throw new InvalidOperationException("USB camera permission was denied.");
        _usbConnection = _usb.OpenDevice(device)
            ?? throw new InvalidOperationException("Android could not open the USB camera.");
        var (bus, address) = UsbPermission.ParseBusAndAddress(device.DeviceName);
        _uvc = new JavaUvcCamera();
        _uvc.Open(device.VendorId, device.ProductId, _usbConnection.FileDescriptor, bus, address, "/dev/bus/usb");
        try { _uvc.SetPreviewSize(1280, 480, 1, 30, JavaUvcCamera.FrameFormatMjpeg, 1.0f); }
        catch { _uvc.SetPreviewSize(1280, 480, 1, 30, JavaUvcCamera.FrameFormatYuyv, 1.0f); }
        _uvcFrames = new UvcFrameListener(_frames.Writer);
        _uvc.SetFrameCallback(_uvcFrames, JavaUvcCamera.PixelFormatRgbx);
        _uvc.StartPreview();
    }

    public async Task<CameraFrame> CaptureAsync(CancellationToken cancellationToken) =>
        await _frames.Reader.ReadAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _uvc?.Dispose(); _uvc = null;
        _uvcFrames?.Dispose(); _uvcFrames = null;
        _usbConnection?.Close(); _usbConnection?.Dispose(); _usbConnection = null;
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
