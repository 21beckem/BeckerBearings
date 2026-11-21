using System.Diagnostics;
using BeckerBearings.App.Services.Calibration;
using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Workflows;

public sealed record DepthCaptureResult(byte[] PreviewBgr, int ValidPixels, TimeSpan ProcessingTime);

/// <summary>Owns the camera-to-depth lifecycle for one capture operation.</summary>
public sealed class CaptureDepthWorkflow(
    ICameraService camera,
    ICalibrationProvider calibration,
    IDepthProcessor depthProcessor)
{
    public async Task<DepthCaptureResult> CaptureAsync(string cameraId, CancellationToken cancellationToken)
    {
        try
        {
            await camera.OpenAsync(cameraId);
            var frame = await camera.CaptureAsync(cancellationToken);
            var calibrationPath = await calibration.GetCalibrationPathAsync(cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            var result = await Task.Run(() => depthProcessor.Process(frame, calibrationPath), cancellationToken);
            stopwatch.Stop();
            return new DepthCaptureResult(result.PreviewBgr, result.ValidPixels, stopwatch.Elapsed);
        }
        finally { await camera.DisposeAsync(); }
    }
}
