using BeckerBearings.App.Services.Workflows;

namespace BeckerBearings.App.Services;

/// <summary>
/// Application-level orchestrator. It composes hardware and processing modules
/// while remaining independent of MAUI views and controls.
/// </summary>
public sealed class AppCoordinator(
    DeviceDiscoveryWorkflow deviceDiscovery,
    CaptureDepthWorkflow depthCapture,
    SerialWorkflow serial,
    ICameraService camera,
    ISerialService serialService) : IAsyncDisposable
{
    public Task<AvailableDevices> RefreshDevicesAsync() => deviceDiscovery.RefreshAsync();

    public Task<DepthCaptureResult> CaptureDepthAsync(string cameraId, CancellationToken cancellationToken) =>
        depthCapture.CaptureAsync(cameraId, cancellationToken);

    public Task OpenSerialAsync(string deviceId, int baudRate) => serial.OpenAsync(deviceId, baudRate);

    public Task<SerialExchangeResult> SendSerialAsync(byte[] data, CancellationToken cancellationToken) =>
        serial.SendAndReadAsync(data, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await camera.DisposeAsync();
        await serialService.DisposeAsync();
    }
}
