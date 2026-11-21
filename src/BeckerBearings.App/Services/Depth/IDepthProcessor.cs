namespace BeckerBearings.App.Services.Depth;

public sealed record DepthProcessingResult(byte[] PreviewBgr, int ValidPixels);

public interface IDepthProcessor
{
    DepthProcessingResult Process(CameraFrame frame, string calibrationPath);
}
