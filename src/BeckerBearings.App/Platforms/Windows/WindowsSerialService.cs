using System.IO.Ports;
using BeckerBearings.App.Services;

namespace BeckerBearings.App.Platforms.Windows;

public sealed class WindowsSerialService : ISerialService
{
    private SerialPort? _port;

    public Task<IReadOnlyList<SerialDevice>> ListAsync() => Task.FromResult<IReadOnlyList<SerialDevice>>(
        SerialPort.GetPortNames().OrderBy(x => x).Select(x => new SerialDevice(x, x)).ToArray());

    public Task OpenAsync(string id, int baudRate)
    {
        _port?.Dispose();
        _port = new SerialPort(id, baudRate) { ReadTimeout = 1500, WriteTimeout = 1500 };
        _port.Open();
        return Task.CompletedTask;
    }

    public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_port is null) throw new InvalidOperationException("Select a serial port first.");
        await _port.BaseStream.WriteAsync(data, cancellationToken);
        await _port.BaseStream.FlushAsync(cancellationToken);
    }

    public async Task<byte[]> ReadAsync(CancellationToken cancellationToken)
    {
        if (_port is null) throw new InvalidOperationException("Select a serial port first.");
        var data = new byte[256];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var count = await _port.BaseStream.ReadAsync(data, timeout.Token);
        return data[..count];
    }

    public ValueTask DisposeAsync() { _port?.Dispose(); return ValueTask.CompletedTask; }
}
