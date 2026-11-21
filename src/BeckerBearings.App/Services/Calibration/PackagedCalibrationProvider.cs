using Microsoft.Maui.Storage;

namespace BeckerBearings.App.Services.Calibration;

/// <summary>Provides a stable on-disk copy of the packaged stereo calibration.</summary>
public sealed class PackagedCalibrationProvider : ICalibrationProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _path;

    public async Task<string> GetCalibrationPathAsync(CancellationToken cancellationToken = default)
    {
        if (_path is not null) return _path;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_path is not null) return _path;
            var destination = Path.Combine(FileSystem.AppDataDirectory, "calibration.json");
            await using var input = await FileSystem.OpenAppPackageFileAsync("calibration.json");
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);
            return _path = destination;
        }
        finally { _gate.Release(); }
    }
}
