using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>Image-only helpers; callers remain responsible for storage and UI.</summary>
public static class ImageTools
{
    public static byte[] ColorDepth(StereoDepthFrame depth)
    {
        var bgr = new byte[depth.PixelCount * 3];
        var maximum = depth.Disparity.Where(value => float.IsFinite(value) && value > 0).DefaultIfEmpty(1).Max();
        for (var index = 0; index < depth.PixelCount; index++)
        {
            if (depth.Confidence[index] == 0 || depth.Disparity[index] <= 0) continue;
            var t = Math.Clamp(depth.Disparity[index] / maximum, 0, 1); var (b, g, r) = Turbo(t);
            bgr[index * 3] = b; bgr[index * 3 + 1] = g; bgr[index * 3 + 2] = r;
        }
        return bgr;
    }

    private static (byte B, byte G, byte R) Turbo(float value)
    {
        // Compact approximation of OpenCV's Turbo LUT, sufficient for diagnostics.
        var r = Math.Clamp(1.5f - Math.Abs(4 * value - 3), 0, 1); var g = Math.Clamp(1.5f - Math.Abs(4 * value - 2), 0, 1); var b = Math.Clamp(1.5f - Math.Abs(4 * value - 1), 0, 1);
        return ((byte)(255 * b), (byte)(255 * g), (byte)(255 * r));
    }
}
