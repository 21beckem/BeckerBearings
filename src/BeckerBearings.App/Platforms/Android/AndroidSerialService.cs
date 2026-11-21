using Android.Content;
using Android.Hardware.Usb;
using Android.App;
using Android.OS;
using BeckerBearings.App.Services;

namespace BeckerBearings.App.Platforms.Android;

public sealed class AndroidSerialService : ISerialService
{
    private readonly UsbManager _manager = (UsbManager)global::Android.App.Application.Context.GetSystemService(Context.UsbService)!;
    private UsbDeviceConnection? _connection;
    private UsbInterface? _dataInterface;
    private UsbEndpoint? _read;
    private UsbEndpoint? _write;

    public Task<IReadOnlyList<SerialDevice>> ListAsync() => Task.FromResult<IReadOnlyList<SerialDevice>>(
        _manager.DeviceList.Values.Where(HasCdcData).Select(d =>
            new SerialDevice(d.DeviceName, $"USB {d.VendorId:X4}:{d.ProductId:X4}")).ToArray());

    private static bool HasCdcData(UsbDevice device) => Enumerable.Range(0, device.InterfaceCount)
        .Any(i => device.GetInterface(i)?.InterfaceClass == UsbClass.CdcData);

    public Task OpenAsync(string id, int baudRate)
    {
        var device = _manager.DeviceList.Values.FirstOrDefault(d => d.DeviceName == id)
            ?? throw new InvalidOperationException("Arduino disconnected.");
        if (!_manager.HasPermission(device))
        {
            var intent = new Intent("com.BeckerBearings.USB_PERMISSION").SetPackage(global::Android.App.Application.Context.PackageName);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S) flags |= PendingIntentFlags.Mutable;
            var pending = PendingIntent.GetBroadcast(global::Android.App.Application.Context, 0, intent, flags);
            _manager.RequestPermission(device, pending);
            throw new InvalidOperationException("Grant USB permission in the Android dialog, then tap Open again.");
        }
        _connection?.Close();
        _connection = _manager.OpenDevice(device) ?? throw new InvalidOperationException("Could not open Arduino USB device.");
        var control = Enumerable.Range(0, device.InterfaceCount).Select(device.GetInterface)
            .FirstOrDefault(i => i?.InterfaceClass == UsbClass.Comm);
        _dataInterface = Enumerable.Range(0, device.InterfaceCount).Select(device.GetInterface)
            .FirstOrDefault(i => i?.InterfaceClass == UsbClass.CdcData)
            ?? throw new InvalidOperationException("No CDC data interface.");
        if (!_connection.ClaimInterface(_dataInterface, true)) throw new InvalidOperationException("Could not claim CDC data interface.");
        if (control is not null) _connection.ClaimInterface(control, true);
        for (var i = 0; i < _dataInterface.EndpointCount; ++i)
        {
            var endpoint = _dataInterface.GetEndpoint(i);
            if (endpoint?.Type != UsbAddressing.XferBulk) continue;
            if (endpoint.Direction == UsbAddressing.In) _read = endpoint;
            else _write = endpoint;
        }
        if (_read is null || _write is null) throw new InvalidOperationException("CDC bulk endpoints missing.");
        var line = BitConverter.GetBytes(baudRate).Concat(new byte[] { 0, 0, 8 }).ToArray();
        _connection.ControlTransfer((UsbAddressing)0x21, 0x20, 0, control?.Id ?? 0, line, line.Length, 1000);
        _connection.ControlTransfer((UsbAddressing)0x21, 0x22, 3, control?.Id ?? 0, null, 0, 1000);
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_connection is null || _write is null) throw new InvalidOperationException("Open Arduino first.");
        var count = _connection.BulkTransfer(_write, data, data.Length, 1500);
        if (count != data.Length) throw new IOException("Incomplete USB serial write.");
        return Task.CompletedTask;
    }

    public Task<byte[]> ReadAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (_connection is null || _read is null) throw new InvalidOperationException("Open Arduino first.");
        var data = new byte[256];
        var count = _connection.BulkTransfer(_read, data, data.Length, 1500);
        return count > 0 ? data[..count] : Array.Empty<byte>();
    }, cancellationToken);

    public ValueTask DisposeAsync() { _connection?.Close(); _connection?.Dispose(); _connection = null; return ValueTask.CompletedTask; }
}
