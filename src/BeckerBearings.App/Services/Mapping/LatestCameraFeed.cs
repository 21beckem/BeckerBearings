using System.Threading.Channels;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>
/// One-slot asynchronous camera feed. Slow processing always receives the most
/// recent frame rather than building latency, as in latest_camera_feed.py.
/// </summary>
public sealed class LatestCameraFeed : IAsyncDisposable
{
    private readonly ICameraService _camera;
    private readonly Channel<CameraFrame> _frames = Channel.CreateBounded<CameraFrame>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _stopping = new();
    private Task? _captureTask;

    public LatestCameraFeed(ICameraService camera) => _camera = camera;

    public async Task StartAsync(string cameraId, CancellationToken cancellationToken)
    {
        if (_captureTask is not null) throw new InvalidOperationException("The camera feed has already started.");
        await _camera.OpenAsync(cameraId);
        _captureTask = Task.Run(() => CaptureLoopAsync(_stopping.Token), CancellationToken.None);
    }

    public ValueTask<CameraFrame> ReadAsync(CancellationToken cancellationToken) => _frames.Reader.ReadAsync(cancellationToken);

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
                await _frames.Writer.WriteAsync(await _camera.CaptureAsync(cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) { _frames.Writer.TryComplete(error); return; }
        finally { _frames.Writer.TryComplete(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        if (_captureTask is not null) try { await _captureTask; } catch (OperationCanceledException) { }
        _stopping.Dispose();
        await _camera.DisposeAsync();
    }
}
