using System.Buffers.Binary;

namespace BeckerBearings.App.Services.Recording;

/// <summary>
/// Portable recorded-camera feed. The archive keeps exact BGR frames and their
/// monotonic capture timestamps; realtime and deterministic replay share it.
/// It is deliberately behind this interface so a platform H.264/MJPEG store can
/// be substituted without changing mapping code.
/// </summary>
public interface IStereoFrameSource : IAsyncDisposable
{
    IAsyncEnumerable<CameraFrame> ReadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class StereoRecordingWriter : IAsyncDisposable
{
    private static readonly byte[] Magic = "BBR1"u8.ToArray();
    private readonly FileStream _stream;
    private readonly int _width, _height;
    private bool _disposed;

    public StereoRecordingWriter(string path, int width = 1280, int height = 480)
    {
        var directory = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        _stream = File.Create(path); _width = width; _height = height;
        _stream.Write(Magic); WriteInt32(_stream, width); WriteInt32(_stream, height);
    }

    public async Task WriteAsync(CameraFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frame.Width != _width || frame.Height != _height || frame.Bgr.Length != _width * _height * 3) throw new ArgumentException("Recording frame dimensions changed.", nameof(frame));
        var metadata = new byte[12]; BinaryPrimitives.WriteDoubleLittleEndian(metadata, frame.TimestampSeconds); BinaryPrimitives.WriteInt32LittleEndian(metadata[8..], frame.Bgr.Length);
        await _stream.WriteAsync(metadata, cancellationToken); await _stream.WriteAsync(frame.Bgr, cancellationToken);
    }

    public async ValueTask DisposeAsync() { if (_disposed) return; _disposed = true; await _stream.DisposeAsync(); }
    private static void WriteInt32(Stream output, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, value); output.Write(bytes); }
}

public sealed class SequentialRecordedCameraFeed : IStereoFrameSource
{
    private readonly string _path;
    public SequentialRecordedCameraFeed(string path) => _path = path;
    public async IAsyncEnumerable<CameraFrame> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(_path); var (width, height) = await StereoRecording.ReadHeaderAsync(stream, cancellationToken);
        while (await StereoRecording.TryReadFrameAsync(stream, width, height, cancellationToken) is { } frame) yield return frame;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class RecordedCameraFeed : IStereoFrameSource
{
    private readonly string _path;
    public RecordedCameraFeed(string path) => _path = path;
    public async IAsyncEnumerable<CameraFrame> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(_path); var (width, height) = await StereoRecording.ReadHeaderAsync(stream, cancellationToken);
        double? firstTimestamp = null; var clock = System.Diagnostics.Stopwatch.StartNew();
        while (await StereoRecording.TryReadFrameAsync(stream, width, height, cancellationToken) is { } frame)
        {
            firstTimestamp ??= frame.TimestampSeconds;
            var due = TimeSpan.FromSeconds(frame.TimestampSeconds - firstTimestamp.Value) - clock.Elapsed;
            if (due > TimeSpan.Zero) await Task.Delay(due, cancellationToken);
            yield return frame;
        }
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class StereoRecording
{
    private static readonly byte[] Magic = "BBR1"u8.ToArray();
    public static async Task<(int Width, int Height)> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[12]; await ReadExactlyAsync(stream, header, cancellationToken);
        if (!header[..4].SequenceEqual(Magic)) throw new InvalidDataException("Not a BeckerBearings BBR recording.");
        var width = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)); var height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        if (width <= 0 || height <= 0) throw new InvalidDataException("Recording dimensions are invalid.");
        return (width, height);
    }
    public static async Task<CameraFrame?> TryReadFrameAsync(Stream stream, int width, int height, CancellationToken cancellationToken)
    {
        var metadata = new byte[12]; var first = await stream.ReadAsync(metadata.AsMemory(0, 1), cancellationToken); if (first == 0) return null;
        await ReadExactlyAsync(stream, metadata.AsMemory(1), cancellationToken);
        var timestamp = BinaryPrimitives.ReadDoubleLittleEndian(metadata); var length = BinaryPrimitives.ReadInt32LittleEndian(metadata.AsSpan(8));
        if (length != width * height * 3) throw new InvalidDataException("Recording frame size is invalid.");
        var bgr = new byte[length]; await ReadExactlyAsync(stream, bgr, cancellationToken); return new CameraFrame(bgr, width, height, timestamp);
    }
    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0; while (offset < buffer.Length) { var read = await stream.ReadAsync(buffer[offset..], cancellationToken); if (read == 0) throw new EndOfStreamException("Recording ended in a partial frame."); offset += read; }
    }
}
