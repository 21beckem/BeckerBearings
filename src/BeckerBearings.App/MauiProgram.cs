using BeckerBearings.App.Services;
using BeckerBearings.App.Services.Calibration;
using BeckerBearings.App.Services.Depth;
using BeckerBearings.App.Services.Workflows;
using Microsoft.Extensions.Logging;

namespace BeckerBearings.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<ICalibrationProvider, PackagedCalibrationProvider>();
        builder.Services.AddSingleton<IDepthProcessor, NativeDepthProcessor>();
        builder.Services.AddSingleton<DeviceDiscoveryWorkflow>();
        builder.Services.AddSingleton<CaptureDepthWorkflow>();
        builder.Services.AddSingleton<SerialWorkflow>();
        builder.Services.AddSingleton<AppCoordinator>();
#if ANDROID
        builder.Services.AddSingleton<ICameraService, Platforms.Android.AndroidCameraService>();
        builder.Services.AddSingleton<ISerialService, Platforms.Android.AndroidSerialService>();
#elif WINDOWS
        builder.Services.AddSingleton<ICameraService, Platforms.Windows.WindowsCameraService>();
        builder.Services.AddSingleton<ISerialService, Platforms.Windows.WindowsSerialService>();
#endif
#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
