using BeckerBearings.App.Services.Depth;
using BeckerBearings.App.Services.Mapping;

namespace BeckerBearings.App.Services.Workflows;

/// <summary>Stateful processing core shared by live, recorded, and offline mapping workflows.</summary>
public sealed class StereoMappingPipeline
{
    private readonly IStereoDepthMapper _depthMapper;
    private readonly string _calibrationPath;
    private readonly GroundPlaneEstimator _ground;
    private readonly PoseTracker _tracker;
    private readonly ObstacleMap _map;
    private readonly Benchmark _benchmark = new Benchmark().Setup("depth", "floor", "tracking", "map");

    public StereoMappingPipeline(IStereoDepthMapper depthMapper, string calibrationPath, MappingOptions options)
    {
        _depthMapper = depthMapper; _calibrationPath = calibrationPath; _ground = new GroundPlaneEstimator(options); _tracker = new PoseTracker(options); _map = new ObstacleMap(options);
    }

    public async Task<MappingFrame> ProcessAsync(CameraFrame rawFrame, CancellationToken cancellationToken)
    {
        return await Task.Run(() => ProcessCore(rawFrame), cancellationToken).ConfigureAwait(false);
    }

    private MappingFrame ProcessCore(CameraFrame rawFrame)
    {
        _benchmark.Start("depth");
        var depth = _depthMapper.Compute(rawFrame, _calibrationPath);
        _benchmark.Stop("depth");
        _benchmark.Start("floor"); var floor = _ground.Fit(depth, rawFrame.TimestampSeconds); _benchmark.Stop("floor");
        _benchmark.Start("tracking"); var tracking = _tracker.Update(depth, floor, rawFrame.TimestampSeconds); _benchmark.Stop("tracking");
        if (tracking.RequestedMapReset) _map.ResetLocal(_tracker.MapEpoch);
        _benchmark.Start("map"); var integration = _map.Integrate(depth, floor, tracking.Pose, rawFrame.TimestampSeconds); var raster = MapRasterizer.Render(_map, 2); _benchmark.Stop("map");
        return new MappingFrame(rawFrame.TimestampSeconds, depth, floor, tracking, integration, raster, _benchmark.Results());
    }
}
