namespace BeckerBearings.App.Services.Workflows;

public sealed record SerialExchangeResult(int BytesSent, byte[] Response);

/// <summary>Provides serial operations without UI state or controls.</summary>
public sealed class SerialWorkflow(ISerialService serial)
{
    public Task OpenAsync(string deviceId, int baudRate) => serial.OpenAsync(deviceId, baudRate);

    public async Task<SerialExchangeResult> SendAndReadAsync(byte[] data, CancellationToken cancellationToken)
    {
        await serial.WriteAsync(data, cancellationToken);
        var response = await serial.ReadAsync(cancellationToken);
        return new SerialExchangeResult(data.Length, response);
    }
}
