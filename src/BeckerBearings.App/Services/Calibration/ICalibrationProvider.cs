namespace BeckerBearings.App.Services.Calibration;

public interface ICalibrationProvider
{
    Task<string> GetCalibrationPathAsync(CancellationToken cancellationToken = default);
}
