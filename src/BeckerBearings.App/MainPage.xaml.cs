using System.Text;
using BeckerBearings.App.Services;

namespace BeckerBearings.App;

public partial class MainPage : ContentPage
{
    private readonly AppCoordinator _app;

    public MainPage(AppCoordinator app)
    { InitializeComponent(); _app = app; }

    private async void RefreshClicked(object sender, EventArgs e)
    {
        try
        {
            var devices = await _app.RefreshDevicesAsync();
            CameraPicker.ItemsSource = devices.Cameras.ToList();
            SerialPicker.ItemsSource = devices.SerialDevices.ToList();
        }
        catch (Exception ex) { DepthStatus.Text = ex.Message; }
    }

    private async void CaptureClicked(object sender, EventArgs e)
    {
        if (CameraPicker.SelectedItem is not VideoDevice selected) { DepthStatus.Text = "Select a camera."; return; }
        try
        {
            DepthStatus.Text = "Opening camera…";
#if ANDROID
            var permission = await Permissions.RequestAsync<Permissions.Camera>();
            if (permission != PermissionStatus.Granted) throw new InvalidOperationException("Camera permission denied.");
#endif
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await _app.CaptureDepthAsync(selected.Id, timeout.Token);
            var bmp = EncodeBmp(result.PreviewBgr);
            DepthPreview.Source = ImageSource.FromStream(() => new MemoryStream(bmp));
            DepthStatus.Text = $"Depth complete: {result.ValidPixels:N0} valid pixels, {result.ProcessingTime.TotalMilliseconds:N0} ms";
        }
        catch (Exception ex) { DepthStatus.Text = ex.Message; }
    }

    private static byte[] EncodeBmp(byte[] bgr)
    {
        const int width = 320, height = 240, stride = width * 3;
        var bmp = new byte[54 + bgr.Length];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(width).CopyTo(bmp, 18);
        BitConverter.GetBytes(height).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(bgr, row * stride, bmp, 54 + (height - 1 - row) * stride, stride);
        return bmp;
    }

    private async void OpenSerialClicked(object sender, EventArgs e)
    {
        if (SerialPicker.SelectedItem is not SerialDevice selected) { SerialStatus.Text = "Select a serial device."; return; }
        try { await _app.OpenSerialAsync(selected.Id, int.Parse(BaudEntry.Text)); SerialStatus.Text = $"Opened {selected.Name}"; }
        catch (Exception ex) { SerialStatus.Text = ex.Message; }
    }

    private async void SendClicked(object sender, EventArgs e)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await _app.SendSerialAsync(Encoding.UTF8.GetBytes(SendEntry.Text ?? ""), timeout.Token);
            SerialStatus.Text = $"Sent {result.BytesSent} bytes; received: {Encoding.UTF8.GetString(result.Response)}";
        }
        catch (Exception ex) { SerialStatus.Text = ex.Message; }
    }
}
