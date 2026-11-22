using System.Runtime.InteropServices;

namespace BeckerBearings.App.Services.Depth;

/// <summary>Managed boundary for the performance-critical native OpenCV pipeline.</summary>
public sealed class NativeDepthProcessor : IDepthProcessor, IStereoDepthMapper, IDisposable
{
    private readonly Dictionary<string, NativeContext> _contexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _contextsGate = new();
    private bool _disposed;
    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr gg_create(string calibrationPath);

    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)]
    private static extern void gg_destroy(IntPtr handle);

    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)]
    private static extern int gg_process(IntPtr handle, byte[] bgr, int width, int height,
        [Out] byte[] previewBgr, out int validPixels);

    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)]
    private static extern int gg_process_full(IntPtr handle, byte[] bgr, int width, int height,
        [Out] float[] disparity, [Out] byte[] confidence, [Out] byte[] gray, [Out] double[] q,
        out int validPixels);

    public DepthProcessingResult Process(CameraFrame frame, string calibrationPath)
    {
        if (frame.Width != 1280 || frame.Height != 480 || frame.Bgr.Length != frame.Width * frame.Height * 3)
            throw new ArgumentException("The stereo calibration requires a 1280×480 BGR frame.", nameof(frame));

        var context = GetContext(calibrationPath);
        lock (context.Gate)
        {
            var preview = new byte[320 * 240 * 3];
            var code = gg_process(context.Handle, frame.Bgr, frame.Width, frame.Height, preview, out var validPixels);
            if (code != 0) throw new InvalidOperationException($"Native stereo processing failed ({code}).");
            return new DepthProcessingResult(preview, validPixels);
        }
    }

    public StereoDepthFrame Compute(CameraFrame frame, string calibrationPath)
    {
        if (frame.Width != 1280 || frame.Height != 480 || frame.Bgr.Length != frame.Width * frame.Height * 3)
            throw new ArgumentException("The stereo calibration requires a 1280×480 BGR frame.", nameof(frame));

        var context = GetContext(calibrationPath);
        lock (context.Gate)
        {
            const int width = 320, height = 240;
            var disparity = new float[width * height];
            var confidence = new byte[width * height];
            var gray = new byte[width * height];
            var q = new double[16];
            var code = gg_process_full(context.Handle, frame.Bgr, frame.Width, frame.Height,
                disparity, confidence, gray, q, out _);
            if (code != 0) throw new InvalidOperationException($"Native stereo processing failed ({code}).");
            return new StereoDepthFrame(disparity, confidence, gray, q, width, height, 48);
        }
    }

    public void Dispose()
    {
        lock (_contextsGate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var context in _contexts.Values) gg_destroy(context.Handle);
            _contexts.Clear();
        }
    }

    private NativeContext GetContext(string calibrationPath)
    {
        var fullPath = Path.GetFullPath(calibrationPath);
        lock (_contextsGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_contexts.TryGetValue(fullPath, out var existing)) return existing;
            var handle = gg_create(fullPath);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("Native OpenCV could not load calibration.");
            return _contexts[fullPath] = new NativeContext(handle);
        }
    }

    private sealed class NativeContext(IntPtr handle)
    {
        public IntPtr Handle { get; } = handle;
        public object Gate { get; } = new();
    }
}
