using System.Runtime.InteropServices;

namespace BeckerBearings.App.Services;

public static class NativeDepth
{
    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr gg_create(string calibrationPath);

    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)]
    private static extern void gg_destroy(IntPtr handle);

    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)]
    private static extern int gg_process(IntPtr handle, byte[] bgr, int width, int height,
        [Out] byte[] previewBgr, out int validPixels);

    public static (byte[] Preview, int ValidPixels) Process(CameraFrame frame, string calibrationPath)
    {
        if (frame.Width != 1280 || frame.Height != 480 || frame.Bgr.Length != frame.Width * frame.Height * 3)
            throw new ArgumentException("The stereo calibration requires a 1280×480 BGR frame.");
        var handle = gg_create(calibrationPath);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Native OpenCV could not load calibration.");
        try
        {
            var preview = new byte[320 * 240 * 3];
            var code = gg_process(handle, frame.Bgr, frame.Width, frame.Height, preview, out var valid);
            if (code != 0) throw new InvalidOperationException($"Native stereo processing failed ({code}).");
            return (preview, valid);
        }
        finally { gg_destroy(handle); }
    }
}
