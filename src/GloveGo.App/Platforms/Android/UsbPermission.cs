using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Android.OS;

namespace GloveGo.App.Platforms.Android;

internal static class UsbPermission
{
    private const string Action = "com.glovego.app.USB_CAMERA_PERMISSION";

    public static async Task<bool> RequestAsync(UsbManager manager, UsbDevice device)
    {
        var context = global::Android.App.Application.Context;
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new PermissionReceiver(result);
        var filter = new IntentFilter(Action);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        else
            context.RegisterReceiver(receiver, filter);
        try
        {
            var intent = new Intent(Action).SetPackage(context.PackageName);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S) flags |= PendingIntentFlags.Mutable;
            using var pending = PendingIntent.GetBroadcast(context, 1, intent, flags);
            manager.RequestPermission(device, pending);
            return await result.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            try { context.UnregisterReceiver(receiver); }
            catch (Java.Lang.IllegalArgumentException) { }
            receiver.Dispose();
        }
    }

    public static (int Bus, int Address) ParseBusAndAddress(string deviceName)
    {
        var parts = deviceName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[^2], out var bus) && int.TryParse(parts[^1], out var address))
            return (bus, address);
        throw new InvalidOperationException($"Android did not provide a USB bus/address for '{deviceName}'.");
    }

    private sealed class PermissionReceiver(TaskCompletionSource<bool> result) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) =>
            result.TrySetResult(intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) == true);
    }
}
