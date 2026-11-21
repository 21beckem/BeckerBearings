namespace BeckerBearings.App.Services.Workflows;

public sealed record AvailableDevices(
    IReadOnlyList<VideoDevice> Cameras,
    IReadOnlyList<SerialDevice> SerialDevices);

/// <summary>Discovers hardware without imposing a presentation model.</summary>
public sealed class DeviceDiscoveryWorkflow(ICameraService camera, ISerialService serial)
{
    public async Task<AvailableDevices> RefreshAsync()
    {
        var cameras = camera.ListAsync();
        var serialDevices = serial.ListAsync();
        await Task.WhenAll(cameras, serialDevices);
        return new AvailableDevices(await cameras, await serialDevices);
    }
}
