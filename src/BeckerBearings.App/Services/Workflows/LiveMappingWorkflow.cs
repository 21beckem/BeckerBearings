using System.Runtime.CompilerServices;
using BeckerBearings.App.Services.Calibration;
using BeckerBearings.App.Services.Depth;
using BeckerBearings.App.Services.Mapping;

namespace BeckerBearings.App.Services.Workflows;

/// <summary>
/// The UI-free counterpart to map_live.py. It owns the capture → depth → floor
/// → pose → grid pipeline and exposes immutable results to any presentation.
/// </summary>
public sealed class LiveMappingWorkflow(
    ICameraService camera,
    ICalibrationProvider calibration,
    IStereoDepthMapper depthMapper,
    MappingOptions options)
{
    public async IAsyncEnumerable<MappingFrame> RunAsync(string cameraId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var calibrationPath = await calibration.GetCalibrationPathAsync(cancellationToken);
        var pipeline = new StereoMappingPipeline(depthMapper, calibrationPath, options);
        await using var feed = new LatestCameraFeed(camera);
        await feed.StartAsync(cameraId, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            var rawFrame = await feed.ReadAsync(cancellationToken);
            yield return await pipeline.ProcessAsync(rawFrame, cancellationToken);
        }
    }
}

/// <summary>Counterpart to map_offline.py and run_map_on_recording.py.</summary>
public sealed class OfflineMappingWorkflow(ICalibrationProvider calibration, IStereoDepthMapper depthMapper, MappingOptions options)
{
    public async IAsyncEnumerable<MappingFrame> RunAsync(IAsyncEnumerable<CameraFrame> frames, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var path = await calibration.GetCalibrationPathAsync(cancellationToken);
        var pipeline = new StereoMappingPipeline(depthMapper, path, options);
        await foreach (var frame in frames.WithCancellation(cancellationToken)) yield return await pipeline.ProcessAsync(frame, cancellationToken);
    }
}

/// <summary>Counterpart to image_depth.py for a caller that already has BGR stereo pixels.</summary>
public sealed class ImageDepthWorkflow(ICalibrationProvider calibration, IStereoDepthMapper depthMapper)
{
    public async Task<StereoDepthFrame> ProcessAsync(CameraFrame frame, CancellationToken cancellationToken)
    {
        var path = await calibration.GetCalibrationPathAsync(cancellationToken);
        return await Task.Run(() => depthMapper.Compute(frame, path), cancellationToken);
    }
}

public sealed record MappingFrame(
    double TimestampSeconds,
    StereoDepthFrame Depth,
    FloorEstimate Floor,
    TrackingResult Tracking,
    MapIntegrationResult Integration,
    MapRaster Map,
    IReadOnlyDictionary<string, BenchmarkResult> Benchmarks);
