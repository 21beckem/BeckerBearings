namespace GloveGo.App.Services;

public sealed record VideoDevice(string Id, string Name);
public sealed record SerialDevice(string Id, string Name);
public sealed record CameraFrame(byte[] Bgr, int Width, int Height, double TimestampSeconds);

public interface ICameraService : IAsyncDisposable
{
    Task<IReadOnlyList<VideoDevice>> ListAsync();
    Task OpenAsync(string id);
    Task<CameraFrame> CaptureAsync(CancellationToken cancellationToken);
}

public interface ISerialService : IAsyncDisposable
{
    Task<IReadOnlyList<SerialDevice>> ListAsync();
    Task OpenAsync(string id, int baudRate);
    Task WriteAsync(byte[] data, CancellationToken cancellationToken);
    Task<byte[]> ReadAsync(CancellationToken cancellationToken);
}
