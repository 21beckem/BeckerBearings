namespace BeckerBearings.App.Services.Depth;

public sealed record DepthProcessingResult(byte[] PreviewBgr, int ValidPixels);

/// <summary>Python's DepthResult equivalent at the depth working resolution.</summary>
public sealed record StereoDepthFrame(
    float[] Disparity,
    byte[] Confidence,
    byte[] Gray,
    double[] Q,
    int Width,
    int Height,
    int MaxDisparity)
{
    public int PixelCount => Width * Height;
}

public interface IDepthProcessor
{
    DepthProcessingResult Process(CameraFrame frame, string calibrationPath);
}

public interface IStereoDepthMapper
{
    StereoDepthFrame Compute(CameraFrame frame, string calibrationPath);
}
