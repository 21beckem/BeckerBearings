using System.Threading.Channels;
using Android.Runtime;
using Java.Nio;
using BeckerBearings.App.Services;

namespace BeckerBearings.App.Platforms.Android;

public sealed class UvcFrameListener(ChannelWriter<CameraFrame> frames) : Java.Lang.Object, Com.Serenegiant.Usb.IFrameCallback
{
    public void OnFrame(ByteBuffer? buffer)
    {
        try
        {
            if (buffer is null) return;
            const int width = 1280, height = 480, rgbxLength = width * height * 4;
            if (buffer.Remaining() < rgbxLength) return;
            var rgbx = new byte[rgbxLength];
            buffer.Get(rgbx);
            var bgr = new byte[width * height * 3];
            for (int source = 0, target = 0; target < bgr.Length; source += 4, target += 3)
            {
                bgr[target] = rgbx[source + 2];
                bgr[target + 1] = rgbx[source + 1];
                bgr[target + 2] = rgbx[source];
            }
            frames.TryWrite(new CameraFrame(bgr, width, height, System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency));
        }
        catch (Java.Lang.Exception) { }
    }
}
